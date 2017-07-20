using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ChakraHost.Hosting;
using Nito.AsyncEx;

namespace ChakraHost
{
    public class Program
    {
        public static void Main(string[] arguments)
        {
            // Execute the entire program in a single-threaded context because Chakra contexts are designed for
            // single -threaded access and JavaScript is naturally single-threaded.
            // Note that AsyncContext comes from the Nito.AsyncEx library by Stephen Cleary.
            AsyncContext.Run(() =>
            {
                var host = new ChakraCoreHost(AsyncContext.Current.Scheduler);
                return host.Run(new[] {"test.js"});
            });
        }
    }

    public class ChakraCoreHost
    {
        private readonly JavaScriptTaskScheduler jsTaskScheduler;
        private readonly HostFunctions hostFunctions = new HostFunctions();
        private readonly HashSet<object> handles = new HashSet<object>(ReferenceEqualsComparer.Instance);

        public ChakraCoreHost(TaskScheduler scheduler)
        {
            this.jsTaskScheduler = new JavaScriptTaskScheduler(scheduler);
        }

        public async Task Run(string[] arguments)
        {
            try
            {
                using (JavaScriptRuntime runtime = JavaScriptRuntime.Create())
                {
                    // Create a context. Note that if we had wanted to start debugging from the very
                    // beginning, we would have called JsStartDebugging right after context is create
                    JavaScriptContext context = runtime.CreateContext();

                    // Now set the execution context as being the current one on this thread.
                    using (new JavaScriptContext.Scope(context))
                    {
                        var hostObject = JavaScriptValue.CreateObject();

                        // Create an object called 'host' and set it on the global object.
                        var hostPropertyId = JavaScriptPropertyId.FromString("host");
                        JavaScriptValue.GlobalObject.SetProperty(hostPropertyId, hostObject, true);

                        // Register a bunch of callbacks on the 'host' object so that the JS code can call into C# code.
                        this.DefineHostCallback(hostObject, "echo", this.hostFunctions.EchoDelegate, IntPtr.Zero);
                        //this.DefineHostCallback(hostObject, "runScript", this.hostFunctions.RunScriptDelegate, IntPtr.Zero);

                        // Now register some async callbacks.
                        this.DefineHostCallback(hostObject, "doSuccessfulWork", this.hostFunctions.DoSuccessfulWorkDelegate, IntPtr.Zero);
                        this.DefineHostCallback(
                            hostObject,
                            "doUnsuccessfulWork",
                            this.hostFunctions.DoUnsuccessfulWorkDelegate,
                            IntPtr.Zero);
                        this.DefineHostCallback(hostObject, "getUrl", this.hostFunctions.GetUrlDelegate, IntPtr.Zero);

                        // Tell Chakra how to handle promises.
                        JavaScriptRuntime.SetPromiseContinuationCallback(this.jsTaskScheduler.PromiseContinuationCallback, IntPtr.Zero);
                        
                        // Everything is setup, so we can go and use the engine now.
                        try
                        {
                            var javaScriptSourceContext = JavaScriptSourceContext.FromIntPtr(IntPtr.Zero);

                            // Load and execute the JavaScript file.
                            var script = File.ReadAllText(arguments[0]);
                            var result = JavaScriptContext.RunScript(
                                script,
                                javaScriptSourceContext + 0,
                                arguments[0]);

                            // Start pumping the task queue so that promise continuations will be processed.
                            // Note that this must be done after the task queue has been initially filled.
                            var completion = this.jsTaskScheduler.PumpMessages();

                            // If the result was a promise, convert it into a C# Task and await its result.
                            // Note that this could be simplified so that the 
                            if (IsPromise(result))
                            {
                                Console.WriteLine("Script returned a promise, awaiting it.");
                                result = await this.ConvertPromiseToTask(result);
                            }

                            Console.WriteLine($"Script result: {result.ConvertToString().ToString()}");

                            // Call the 'sayHello' method on the object which was returned from the script.
                            await this.ConvertPromiseToTask(
                                result.GetProperty(JavaScriptPropertyId.FromString("sayHello"))
                                      .CallFunction(JavaScriptValue.GlobalObject, JavaScriptValue.FromString("do a barrel roll!")));

                            // Call the 'add' method, which is not an async method
                            var addResult = result.GetProperty(JavaScriptPropertyId.FromString("add"))
                                                  .CallFunction(
                                                      JavaScriptValue.GlobalObject,
                                                      JavaScriptValue.FromInt32(78),
                                                      JavaScriptValue.FromInt32(22));

                            Console.WriteLine($"In C# land: 78 + 22 = {addResult.ConvertToNumber().ToDouble()}");

                            // Wait for the task pump to complete.
                            await completion;

                        }
                        catch (JavaScriptScriptException exception)
                        {
                            var messageValue = exception.Error.GetProperty(JavaScriptPropertyId.FromString("message"));
                            Console.Error.WriteLine("exception: {0}", messageValue.ToString());
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine("failed to run script: {0}", e.Message);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("fatal error: internal error: {0}.", e.Message);
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        private void DefineHostCallback(
            JavaScriptValue globalObject,
            string callbackName,
            JavaScriptNativeFunction callback,
            IntPtr callbackData)
        {
            var propertyId = JavaScriptPropertyId.FromString(callbackName);
            this.handles.Add(callback);
            var function = JavaScriptValue.CreateFunction(callback, callbackData);
            globalObject.SetProperty(propertyId, function, true);
        }

        private void DefineHostCallback(
            JavaScriptValue globalObject,
            string callbackName,
            AsyncJavaScriptNativeFunction callback, // Note: this one is async
            IntPtr callbackData)
        {
            var propertyId = JavaScriptPropertyId.FromString(callbackName);

            // Create a promise-returning function from our Task-returning function.
            JavaScriptNativeFunction nativeFunction = (callee, call, arguments, count, data) => this.jsTaskScheduler.CreatePromise(
                callback(callee, call, arguments, count, data));
            this.handles.Add(nativeFunction);
            var function = JavaScriptValue.CreateFunction(
                nativeFunction,
                callbackData);

            globalObject.SetProperty(propertyId, function, true);
        }

        private static bool IsPromise(JavaScriptValue value)
        {
            var id = JavaScriptPropertyId.FromString("then");
            return value.HasProperty(id) &&
                   value.GetProperty(id).ValueType == JavaScriptValueType.Function;
        }

        private Task<JavaScriptValue> ConvertPromiseToTask(JavaScriptValue promise)
        {
            // Get the 'then' function from the promise.
            var thenFunction = promise.GetProperty(JavaScriptPropertyId.FromString("then"));

            // Create resolve & reject callbacks and wire them to a TaskCompletionSource
            JavaScriptNativeFunction[] state = new JavaScriptNativeFunction[2];
            TaskCompletionSource<JavaScriptValue> completion = new TaskCompletionSource<JavaScriptValue>(state);
            var resolveFunc = JavaScriptValue.CreateFunction(
                state[0] = (callee, call, arguments, count, data) =>
                {
                    if (count > 1)
                    {
                        completion.TrySetResult(arguments[1]);
                    }
                    else
                    {
                        completion.TrySetResult(JavaScriptValue.Undefined);
                    }

                    return JavaScriptValue.Invalid;
                });
            var rejectFunc = JavaScriptValue.CreateFunction(
                state[1] = (callee, call, arguments, count, data) =>
                {
                    if (count > 1)
                    {
                        completion.TrySetException(new JavaScriptException(arguments[1]));
                    }
                    else
                    {
                        completion.TrySetException(
                            new JavaScriptException(JavaScriptValue.FromString("Unknown exception in JavaScript promise rejection.")));
                    }
                    return JavaScriptValue.Invalid;
                });

            // Register the callbacks with the promise using the 'then' function.
            thenFunction.CallFunction(promise, resolveFunc, rejectFunc);
            
            // Return a task which will complete when the promise completes.
            return completion.Task;
        }
    }
}