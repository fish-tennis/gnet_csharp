using System;

namespace gnet_csharp
{
    /// <summary>
    /// 数组切片,类似golang的slice
    /// 当需要对一个数组的中间数据操作时,可以简化接口,省掉一些拷贝操作
    /// </summary>
    public class Slice<T>
    {
        private T[] m_Array;

        /// <summary>
        /// [begin,end)
        /// </summary>
        private int m_StartIndex;

        private int m_Length;

        public int Length => m_Length;

        public int StartIndex => m_StartIndex;

        public T[] OriginalArray => m_Array;

        public Slice(T[] buffer)
        {
            m_Array = buffer;
            m_StartIndex = 0;
            m_Length = buffer.Length;
        }

        public Slice(T[] buffer, int startIndex, int length)
        {
            m_Array = buffer;
            m_StartIndex = startIndex;
            m_Length = length;
        }

        public Slice(Slice<T> src, int begin, int length)
        {
            m_Array = src.m_Array;
            m_StartIndex = src.m_StartIndex + begin;
            m_Length = length;
        }

        public void CopyTo(T[] dst, int dstIndex, int length)
        {
            Array.Copy(m_Array, m_StartIndex, dst, dstIndex, length);
        }

        /// <summary>
        /// 拷贝一份数据
        /// </summary>
        /// <returns></returns>
        public T[] ToArray()
        {
            var newArray = new T[m_Length];
            Array.Copy(m_Array, m_StartIndex, newArray, 0, m_Length);
            return newArray;
        }
    }
}