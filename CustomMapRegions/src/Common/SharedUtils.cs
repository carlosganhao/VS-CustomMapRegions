using System;
using Vintagestory.API.Datastructures;

namespace CustomMapRegions.Common;

public static class SharedUtils
{
    public static void SafeDequeueThrough<T>(UniqueQueue<T> queue, object qlock, Action<T> onDequeueAction, bool closingCondition = false)
    {
        if(queue.Count > 0)
        {
            int q = queue.Count;
            while(q-- > 0)
            {
                T temp;

                if (closingCondition) break;

                lock (qlock)
                {
                    if(queue.Count <= 0) break;
                    temp = queue.Dequeue();
                }
                
                onDequeueAction.Invoke(temp);
            }
        }
    }
}