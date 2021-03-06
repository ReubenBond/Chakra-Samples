using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChakraHost
{
    internal class ReferenceEqualsComparer : IEqualityComparer<object>
    {
        /// <summary>
        /// Gets an instance of this class.
        /// </summary>
        public static ReferenceEqualsComparer Instance { get; } = new ReferenceEqualsComparer();
        
        bool IEqualityComparer<object>.Equals(object x, object y)
        {
            return object.ReferenceEquals(x, y);
        }

        int IEqualityComparer<object>.GetHashCode(object obj)
        {
            return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
        }
    }
}