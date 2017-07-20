using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ChakraHost.Hosting;

namespace ChakraHost
{
    internal class HostFunctions
    {
        private static JavaScriptValue Echo(
            JavaScriptValue callee,
            bool isConstructCall,
            JavaScriptValue[] arguments,
            ushort argumentCount,
            IntPtr callbackData)
        {
            for (uint index = 1; index < argumentCount; index++)
            {
                if (index > 1)
                {
                    Console.Write(" ");
                }

                Console.Write(arguments[index].ConvertToString().ToString());
            }

            Console.WriteLine();

            return JavaScriptValue.Invalid;
        }

        private static async Task<JavaScriptValue> DoSuccessfulWork(
            JavaScriptValue callee,
            bool isConstructCall,
            JavaScriptValue[] arguments,
            ushort argumentCount,
            IntPtr callbackData)
        {
            await Task.Delay(200);
            return JavaScriptValue.FromString("promise from native code");
        }

        private static async Task<JavaScriptValue> DoUnsuccessfulWork(
            JavaScriptValue callee,
            bool isConstructCall,
            JavaScriptValue[] arguments,
            ushort argumentCount,
            IntPtr callbackData)
        {
            await Task.Delay(200);
            throw new UnauthorizedAccessException("promise from native code");
        }
        
        public JavaScriptNativeFunction EchoDelegate { get; } = Echo;
        //public JavaScriptNativeFunction RunScriptDelegate { get; } = RunScript;
        public AsyncJavaScriptNativeFunction DoSuccessfulWorkDelegate { get; } = DoSuccessfulWork; // we've made this one async :)
        public AsyncJavaScriptNativeFunction DoUnsuccessfulWorkDelegate { get; } = DoUnsuccessfulWork;
        
        public AsyncJavaScriptNativeFunction GetUrlDelegate { get; } = async (callee, call, arguments, count, data) =>
        {
            if (count > 1)
            {
                var url = arguments[1];
                var result = await new HttpClient().GetStringAsync(url.ConvertToString().ToString());
                return JavaScriptValue.FromString(result);
            }

            throw new Exception("Oi! Call me with a URL!");
        };
/*
        public static JavaScriptValue RunScript(
            JavaScriptValue callee,
            bool isConstructCall,
            JavaScriptValue[] arguments,
            ushort argumentCount,
            IntPtr callbackData)
        {
            if (argumentCount < 2)
            {
                ThrowException("not enough arguments");
                return JavaScriptValue.Invalid;
            }

            var filename = arguments[1].ToString();
            var script = File.ReadAllText(filename);
            if (string.IsNullOrEmpty(script))
            {
                ThrowException("invalid script");
                return JavaScriptValue.Invalid;
            }

            return JavaScriptContext.RunScript(script, JavaScriptSourceContext.FromIntPtr(IntPtr.Zero), filename);
        }*/

        private static void ThrowException(string errorString)
        {
            // We ignore error since we're already in an error state.
            JavaScriptValue errorValue = JavaScriptValue.FromString(errorString);
            JavaScriptValue errorObject = JavaScriptValue.CreateError(errorValue);
            JavaScriptContext.SetException(errorObject);
        }
    }
}