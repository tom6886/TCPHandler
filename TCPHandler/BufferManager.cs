using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace TCPHandler
{
    internal sealed class BufferManager : IDisposable
    {
        private Byte[] buffer;
        private Int32 bufferSize;
        private Int32 numSize;
        private Int32 currentIndex;
        private Stack<Int32> freeIndexPool;

        internal BufferManager(Int32 numSize, Int32 bufferSize)
        {
            this.bufferSize = bufferSize;
            this.numSize = numSize;
            this.currentIndex = 0;
            this.freeIndexPool = new Stack<Int32>();
        }

        internal void InitBuffer()
        {
            this.buffer = new Byte[this.numSize];
        }

        internal Boolean SetBuffer(SocketAsyncEventArgs args)
        {
            if (this.freeIndexPool.Count > 0)
            {
                args.SetBuffer(this.buffer, this.freeIndexPool.Pop(), this.bufferSize);
            }
            else
            {
                if ((this.numSize - this.bufferSize) < this.currentIndex)
                {
                    return false;
                }
                args.SetBuffer(this.buffer, this.currentIndex, this.bufferSize);
                this.currentIndex += this.bufferSize;
            }
            return true;
        }

        internal void FreeBuffer(SocketAsyncEventArgs args)
        {
            this.freeIndexPool.Push(args.Offset);
            args.SetBuffer(null, 0, 0);
        }

        #region IDisposable Members

        public void Dispose()
        {
            buffer = null;
            freeIndexPool = null;
        }

        #endregion
    }
}
