namespace ObjectTreeWalker
{
    /// <summary>
    /// Type of the member, useful for iteration filtering
    /// </summary>
    public enum MemberType
    {
        /// <summary>
        /// Indicates that the type is not set (default value intended to debug possible issues)
        /// </summary>
        NotSet,

        /// <summary>
        /// Indicates that a member is a field
        /// </summary>
        Field,

        /// <summary>
        /// Indicates that a member is a property
        /// </summary>
        Property,

        /// <summary>
        /// Indicates that a member is an item in a collection
        /// </summary>
        CollectionItem
    }
}
