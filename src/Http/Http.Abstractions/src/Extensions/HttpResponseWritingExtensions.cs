// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Http
{
    /// <summary>
    /// Convenience methods for writing to the response.
    /// </summary>
    public static class HttpResponseWritingExtensions
    {
        /// <summary>
        /// Writes the given text to the response body. UTF-8 encoding will be used.
        /// </summary>
        /// <param name="response">The <see cref="HttpResponse"/>.</param>
        /// <param name="text">The text to write to the response.</param>
        /// <param name="cancellationToken">Notifies when request operations should be cancelled.</param>
        /// <returns>A task that represents the completion of the write operation.</returns>
        public static Task WriteAsync(this HttpResponse response, string text, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return response.WriteAsync(text, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Writes the given text to the response body using the given encoding.
        /// </summary>
        /// <param name="response">The <see cref="HttpResponse"/>.</param>
        /// <param name="text">The text to write to the response.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="cancellationToken">Notifies when request operations should be cancelled.</param>
        /// <returns>A task that represents the completion of the write operation.</returns>
        public static Task WriteAsync(this HttpResponse response, string text, Encoding encoding, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            if (!response.HasStarted)
            {
                var startAsyncTask = response.StartAsync(cancellationToken);
                if (!startAsyncTask.IsCompletedSuccessfully)
                {
                    return StartAndWriteAsyncAwaited(response, text, encoding, cancellationToken, startAsyncTask);
                }
            }

            Write(response, text, encoding);

            var flushAsyncTask = response.BodyPipe.FlushAsync(cancellationToken);
            if (flushAsyncTask.IsCompleted)
            {
                flushAsyncTask.GetAwaiter().GetResult();
                return Task.CompletedTask;
            }

            return flushAsyncTask.AsTask();
        }

        private static async Task StartAndWriteAsyncAwaited(this HttpResponse response, string text, Encoding encoding, CancellationToken cancellationToken, Task startAsyncTask)
        {
            await startAsyncTask;
            Write(response, text, encoding);
            await response.BodyPipe.FlushAsync(cancellationToken);
        }

        private static void Write(this HttpResponse response, string text, Encoding encoding)
        {
            var pipeWriter = response.BodyPipe;
            var encodedLength = encoding.GetByteCount(text);
            var destination = pipeWriter.GetSpan();

            if (encodedLength <= destination.Length)
            {
                var bytesWritten = encoding.GetBytes(text, destination);
                pipeWriter.Advance(bytesWritten);
            }
            else
            {
                WriteMutliSegmentEncoded(pipeWriter, text, encoding, destination);
            }
        }

        private static void WriteMutliSegmentEncoded(PipeWriter writer, string text, Encoding encoding, Span<byte> destination)
        {
            var encoder = encoding.GetEncoder();
            var readOnlySpan = text.AsSpan();
            var completed = false;
            while (!completed)
            {
                encoder.Convert(readOnlySpan, destination, readOnlySpan.Length == 0, out var charsUsed, out var bytesUsed, out completed);

                writer.Advance(bytesUsed);
                if (completed)
                {
                    return;
                }
                readOnlySpan = readOnlySpan.Slice(charsUsed);

                destination = writer.GetSpan(readOnlySpan.Length);
            }
        }
    }
}
