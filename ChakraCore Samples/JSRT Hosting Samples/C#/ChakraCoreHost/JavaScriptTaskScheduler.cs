using System.Collections.Generic;
using System.Threading.Tasks;
using ChakraCoreHost.Tasks;
using ChakraHost.Hosting;
using Nito.AsyncEx;

namespace ChakraHost
{
    public class JavaScriptTaskScheduler
    {
        private readonly AsyncManualResetEvent queueEvent = new AsyncManualResetEvent(false);
        private volatile int outstandingItems = 0;
        private readonly Queue<TaskItem> taskQueue = new Queue<TaskItem>();
        private readonly object taskSync = new object();
        private TaskScheduler taskScheduler;

        public JavaScriptTaskScheduler(TaskScheduler taskScheduler)
        {
            this.taskScheduler = taskScheduler;
            this.PromiseContinuationCallback = (task, callbackState) => this.Enqueue(new JsTaskItem(task));
        }

        public JavaScriptPromiseContinuationCallback PromiseContinuationCallback { get; }

        public void Enqueue(TaskItem item)
        {
            lock (this.taskSync)
            {
                this.taskQueue.Enqueue(item);
            }

            this.queueEvent.Set();
        }

        private TaskItem Dequeue()
        {
            TaskItem item = null;
            lock (this.taskSync)
            {
                item = this.taskQueue.Dequeue();
            }

            this.queueEvent.Reset();

            return item;
        }

        public async Task PumpMessages()
        {
            while (true)
            {
                bool hasTasks;
                bool hasOutstandingItems;

                lock (this.taskSync)
                {
                    hasTasks = this.taskQueue.Count > 0;
                    hasOutstandingItems = this.outstandingItems > 0;
                }

                if (hasTasks)
                {
                    var task = this.Dequeue();
                    task.Run();
                    task.Dispose();
                }
                else if (hasOutstandingItems)
                {
                    await this.queueEvent.WaitAsync();
                }
                else
                {
                    break;
                }
            }
        }

        public JavaScriptValue CreatePromise(Task<JavaScriptValue> task)
        {
            lock (taskSync)
            {
                outstandingItems++;
            }

            JavaScriptValue resolve;
            JavaScriptValue reject;
            JavaScriptValue promise = JavaScriptValue.CreatePromise(out resolve, out reject);
            reject.AddRef();
            resolve.AddRef();
            task.ContinueWith(
                (antecedent, state) =>
                {
                    switch (antecedent.Status)
                    {
                        case TaskStatus.Canceled:
                        case TaskStatus.Faulted:
                            reject.CallFunction(
                                JavaScriptValue.GlobalObject,
                                JavaScriptValue.CreateError(JavaScriptValue.FromString(antecedent.Exception.Message)));
                            break;
                        case TaskStatus.RanToCompletion:
                            var result = antecedent.GetAwaiter().GetResult();
                            resolve.CallFunction(JavaScriptValue.GlobalObject, result);
                            break;
                    }

                    lock (taskSync)
                    {
                        outstandingItems--;
                    }
                    reject.Release();
                    resolve.Release();
                },
                null,
                this.taskScheduler);
            return promise;
        }
    }
}