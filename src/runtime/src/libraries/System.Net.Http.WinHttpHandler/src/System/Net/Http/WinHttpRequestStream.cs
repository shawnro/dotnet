// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SafeWinHttpHandle = Interop.WinHttp.SafeWinHttpHandle;

#pragma warning disable CA1844 // lack of WriteAsync(ReadOnlyMemory) override in .NET Standard 2.1 build

namespace System.Net.Http
{
    internal sealed class WinHttpRequestStream : Stream
    {
        private static readonly byte[] s_crLfTerminator = "\r\n"u8.ToArray();
        private static readonly byte[] s_endChunk = "0\r\n\r\n"u8.ToArray();

        private volatile bool _disposed;
        private readonly WinHttpRequestState _state;
        private readonly SafeWinHttpHandle _requestHandle;
        private readonly WinHttpChunkMode _chunkedMode;

        internal WinHttpRequestStream(WinHttpRequestState state, WinHttpChunkMode chunkedMode)
        {
            _state = state;
            _chunkedMode = chunkedMode;

            // Take copy of handle from state.
            // The state's request handle will be set to null once the response stream starts.
            Debug.Assert(_state.RequestHandle != null);
            _requestHandle = _state.RequestHandle;
        }

        public override bool CanRead
        {
            get
            {
                return false;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return !_disposed;
            }
        }

        public override long Length
        {
            get
            {
                CheckDisposed();
                throw new NotSupportedException();
            }
        }

        public override long Position
        {
            get
            {
                CheckDisposed();
                throw new NotSupportedException();
            }

            set
            {
                CheckDisposed();
                throw new NotSupportedException();
            }
        }

        public override void Flush()
        {
            // Nothing to do.
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return cancellationToken.IsCancellationRequested ?
                Task.FromCanceled(cancellationToken) :
                Task.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count > buffer.Length - offset)
            {
                throw new ArgumentException(SR.net_http_buffer_insufficient_length, nameof(buffer));
            }

            if (token.IsCancellationRequested)
            {
                var tcs = new TaskCompletionSource<int>();
                tcs.TrySetCanceled(token);
                return tcs.Task;
            }

            CheckDisposed();

            if (_state.TcsInternalWriteDataToRequestStream != null &&
                !_state.TcsInternalWriteDataToRequestStream.Task.IsCompleted)
            {
                throw new InvalidOperationException(SR.net_http_no_concurrent_io_allowed);
            }

            return InternalWriteAsync(buffer, offset, count, token);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? asyncCallback, object? asyncState) =>
            TaskToAsyncResult.Begin(WriteAsync(buffer, offset, count, CancellationToken.None), asyncCallback, asyncState);

        public override void EndWrite(IAsyncResult asyncResult) =>
            TaskToAsyncResult.End(asyncResult);

        public override long Seek(long offset, SeekOrigin origin)
        {
            CheckDisposed();
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            CheckDisposed();
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            throw new NotSupportedException();
        }

        internal async Task EndUploadAsync(CancellationToken token)
        {
            switch (_chunkedMode)
            {
                case WinHttpChunkMode.Manual:
                    await InternalWriteDataAsync(s_endChunk, 0, s_endChunk.Length, token).ConfigureAwait(false);
                    break;
                case WinHttpChunkMode.Automatic:
                    // Send empty DATA frame with END_STREAM flag.
                    await InternalWriteEndDataAsync(token).ConfigureAwait(false);
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(this.GetType().FullName);
            }
        }

        private Task InternalWriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            if (count == 0)
            {
                return Task.CompletedTask;
            }

            return _chunkedMode == WinHttpChunkMode.Manual ?
                InternalWriteChunkedModeAsync(buffer, offset, count, token) :
                InternalWriteDataAsync(buffer, offset, count, token);
        }

        private async Task InternalWriteChunkedModeAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            // WinHTTP does not fully support chunked uploads. It simply allows one to omit the 'Content-Length' header
            // and instead use the 'Transfer-Encoding: chunked' header. The caller is still required to encode the
            // request body according to chunking rules.
            Debug.Assert(_chunkedMode == WinHttpChunkMode.Manual);
            Debug.Assert(count > 0);

            byte[] chunkSize = Encoding.UTF8.GetBytes($"{count:x}\r\n");

            await InternalWriteDataAsync(chunkSize, 0, chunkSize.Length, token).ConfigureAwait(false);

            await InternalWriteDataAsync(buffer, offset, count, token).ConfigureAwait(false);
            await InternalWriteDataAsync(s_crLfTerminator, 0, s_crLfTerminator.Length, token).ConfigureAwait(false);
        }

        private Task<bool> InternalWriteDataAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            Debug.Assert(count > 0);

            _state.PinSendBuffer(buffer);
            _state.TcsInternalWriteDataToRequestStream =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_state.Lock)
            {
                if (!Interop.WinHttp.WinHttpWriteData(
                    _requestHandle,
                    Marshal.UnsafeAddrOfPinnedArrayElement(buffer, offset),
                    (uint)count,
                    IntPtr.Zero))
                {
                    _state.TcsInternalWriteDataToRequestStream.TrySetException(
                        new IOException(SR.net_http_io_write, WinHttpException.CreateExceptionUsingLastError(nameof(Interop.WinHttp.WinHttpWriteData))));
                }
            }

            return _state.TcsInternalWriteDataToRequestStream.Task;
        }

        private Task<bool> InternalWriteEndDataAsync(CancellationToken token)
        {
            _state.TcsInternalWriteDataToRequestStream =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_state.Lock)
            {
                if (!Interop.WinHttp.WinHttpWriteData(
                    _requestHandle,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero))
                {
                    _state.TcsInternalWriteDataToRequestStream.TrySetException(
                        new IOException(SR.net_http_io_write, WinHttpException.CreateExceptionUsingLastError(nameof(Interop.WinHttp.WinHttpWriteData))));
                }
            }

            return _state.TcsInternalWriteDataToRequestStream.Task;
        }
    }
}
