using System;
using System.IO;
using System.Net;
using System.Net.Http;

namespace FastDM
{
    public static class HttpRangeValidation
    {
        public static bool IsProbeResponse(HttpResponseMessage response, out long totalBytes)
        {
            totalBytes = 0;
            if (response.StatusCode != HttpStatusCode.PartialContent) return false;
            var range = response.Content.Headers.ContentRange;
            if (range == null || !string.Equals(range.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
                range.From != 0 || range.To != 0 || range.Length is not > 0)
                throw new InvalidDataException("The server returned an invalid byte-range probe response.");
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != 1)
                throw new InvalidDataException("The server returned an unexpected byte-range probe length.");
            totalBytes = range.Length.Value;
            return true;
        }

        public static long ValidateSegmentResponse(HttpResponseMessage response, long start, long end, long totalBytes)
        {
            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new InvalidDataException("The server did not return a partial response for a range request.");

            var range = response.Content.Headers.ContentRange;
            if (range == null || !string.Equals(range.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
                range.From != start || range.To != end || range.Length != totalBytes)
                throw new InvalidDataException("The server returned a mismatched byte range; the partial file was not completed.");

            long expected = checked(end - start + 1);
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != expected)
                throw new InvalidDataException("The server returned an unexpected byte count for a range.");
            return expected;
        }
    }
}
