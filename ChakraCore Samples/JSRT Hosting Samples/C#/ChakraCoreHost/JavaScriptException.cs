using System;
using ChakraHost.Hosting;

namespace ChakraHost
{
    public class JavaScriptException : Exception
    {
        private readonly JavaScriptValue error;

        public JavaScriptException(JavaScriptValue error) : base(error.ConvertToString().ToString())
        {
            this.error = error;
        }

        public JavaScriptValue Error => this.error;
    }
}