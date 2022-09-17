using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace ObjectTreeWalker
{
	/// <summary>
	/// A class that allows recursively iterating over object properties and fields
	/// </summary>
	public class ObjectIterator
	{
		private static readonly ObjectPool<Queue<KeyValuePair<string, object>>> TraversalQueuePool =
			new DefaultObjectPoolProvider().Create<Queue<KeyValuePair<string, object>>>();

		/* Unmerged change from project 'ObjectTreeWalker(net6.0)'
		Before:
			}
		After:
			}
		*/
	}
}
