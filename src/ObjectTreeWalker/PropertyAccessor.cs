using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using System.Text;

namespace ObjectTreeWalker
{
	/// <summary>
	/// A helper class that replaces reflection access to properties and fields
	/// </summary>
	/// <typeparam name="TObject">Type of the object to generate access to</typeparam>
	// credit: inspired by https://stackoverflow.com/a/39602196/320103
	internal readonly struct PropertyAccessor<TObject>
	{
		private readonly Func<TObject, object> _getMethod;
		private readonly Action<TObject, object> _setMethod;

		/// <summary>
		/// Gets the reflection info of the property
		/// </summary>
		public PropertyInfo PropertyInfo { get; }

		/// <summary>
		/// Gets the name of the property
		/// </summary>
		public string Name => PropertyInfo.Name;

		/// <summary>
		/// Initializes a new instance of the <see cref="PropertyAccessor{TObject}"/> struct
		/// </summary>
		/// <param name="propertyInfo">reflection info of the property to create accessor of</param>
		public PropertyAccessor(PropertyInfo propertyInfo)
			: this()
		{
			PropertyInfo = propertyInfo;
			_getMethod = CreateGetPropertyFunc(propertyInfo);
			_setMethod = CreateSetPropertyFunc(propertyInfo);
		}

		public object GetValue(TObject source) =>
			_getMethod(source);

		public void SetValue(TObject source, object value) =>
			_setMethod(source, value);

		private static Func<TObject, object> CreateGetPropertyFunc(PropertyInfo propertyInfo)
		{
			var parameterExpression = Expression.Parameter(typeof(TObject));
			return Expression.Lambda<Func<TObject, object>>(
				Expression.Convert(Expression.Property(parameterExpression, propertyInfo), typeof(object)), parameterExpression)
				.Compile();
		}

		private static Action<TObject, object> CreateSetPropertyFunc(PropertyInfo propertyInfo)
		{
			var parameter1Expression = Expression.Parameter(typeof(TObject));
			var parameter2Expression = Expression.Parameter(typeof(object));

			var propertyAccessExpression = Expression.Property(parameter1Expression, propertyInfo);
			return Expression
				.Lambda<Action<TObject, object>>(
					Expression.Assign(propertyAccessExpression, Expression.Convert(parameter2Expression, propertyInfo.PropertyType)),
					parameter1Expression,
					parameter2Expression).Compile();
		}
	}
}
