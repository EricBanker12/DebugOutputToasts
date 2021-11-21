using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DebugOutputToasts
{
    public class DebugOutputMonitor : IDisposable
    {
        private const string DBWIN_BUFFER = @"DBWIN_BUFFER";
        private const string DBWIN_BUFFER_READY = @"DBWIN_BUFFER_READY";
        private const string DBWIN_DATA_READY = @"DBWIN_DATA_READY";

        private MemoryMappedFile dbwin_buffer;
        private EventWaitHandle dbwin_buffer_ready;
        private EventWaitHandle dbwin_data_ready;
        private Queue<MemoryStream> dbwin_queue;

        private bool disposedValue;
        private CancellationTokenSource cancellation;

        public struct DebugOutput
        {
            public uint dwProcessId;
            public string outputDebugString;
        }

        /// <summary>
        /// Creates a OutputDebugString monitor, which calls an action every message.
        /// </summary>
        /// <param name="action"></param>
        public DebugOutputMonitor(Action<DebugOutput> action)
        {
            dbwin_queue = new Queue<MemoryStream>();
            
            dbwin_buffer = MemoryMappedFile.CreateNew(DBWIN_BUFFER, 4096, MemoryMappedFileAccess.ReadWrite);
            dbwin_buffer_ready = new EventWaitHandle(true, EventResetMode.AutoReset, DBWIN_BUFFER_READY);
            dbwin_data_ready = new EventWaitHandle(false, EventResetMode.AutoReset, DBWIN_DATA_READY);

            cancellation = new CancellationTokenSource();
            ActionMonitor(action, cancellation.Token);
            Task.Run(() => BufferMonitor(cancellation.Token));
        }

        private async void ActionMonitor(Action<DebugOutput> action, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (dbwin_queue.Any())
                    {
                        using (MemoryStream dataStream = dbwin_queue.Dequeue())
                        {
                            byte[] data = dataStream.GetBuffer();

                            uint dwProcessId = BitConverter.ToUInt32(data, 0);
                            string outputDebugString = Encoding.ASCII.GetString(data, 4, 4096 - 4);
                            
                            int length = outputDebugString.IndexOf('\0');
                            if (length > 0) outputDebugString = outputDebugString.Substring(0, length);
                            
                            action(new DebugOutput { dwProcessId = dwProcessId, outputDebugString = outputDebugString });
                        }
                    }
                    await Task.Delay(1);
                }
            }
            catch (Exception e)
            {
                throw;
            }
        }

        private void BufferMonitor(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (dbwin_data_ready.WaitOne(100))
                    {
                        using (var reader = dbwin_buffer.CreateViewStream())
                        {
                            MemoryStream data = new MemoryStream();
                            reader.CopyTo(data);
                            dbwin_queue.Enqueue(data);
                            dbwin_buffer_ready.Set();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    cancellation.Cancel();

                    // TODO: dispose managed state (managed objects)
                    dbwin_buffer_ready.Dispose();
                    dbwin_data_ready.Dispose();
                    dbwin_buffer.Dispose();
                    
                    while (dbwin_queue.Any())
                    {
                        dbwin_queue.Dequeue().Dispose();
                    }
                    dbwin_queue = null;

                    cancellation.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        //~DebugOutputMonitor()
        //{
        //    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //    Dispose(disposing: false);
        //}

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
