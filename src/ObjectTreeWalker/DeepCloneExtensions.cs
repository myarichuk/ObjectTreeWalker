#pragma warning disable CS1591
using System;

namespace ObjectTreeWalker
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class DeepCloneableAttribute : Attribute
    {
    }

    public static class DeepCloneExtensions
    {
    }
}
