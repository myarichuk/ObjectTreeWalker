// Copyright (c) Michael Yarichuk. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETSTANDARD2_1
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Polyfill required by the compiler for init-only setters and positional records on netstandard2.1
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
