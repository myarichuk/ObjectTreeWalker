#pragma warning disable CS1591
using System;

namespace ObjectTreeWalker
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class ObjectWalkableAttribute : Attribute
    {
    }

    public interface IObjectVisitor
    {
        void Visit(object obj);
    }
}
