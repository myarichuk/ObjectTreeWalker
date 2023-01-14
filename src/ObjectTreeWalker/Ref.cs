namespace ObjectTreeWalker
{
    /// <summary>
    /// A class to hold references for a struct
    /// </summary>
    /// <typeparam name="TRef">Type of the struct to hold reference for</typeparam>
    internal class Ref<TRef>
        where TRef : struct
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Ref{TRef}"/> class
        /// </summary>
        /// <param name="ref">type of the struct to hold reference for</param>
        public Ref(in TRef @ref)
        {
            Value = @ref;
        }

        /// <summary>
        /// Gets the value of the struct the class hold reference for
        /// </summary>
        public TRef Value { get; }
    }
}
