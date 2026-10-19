using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Globalization;

namespace FastDM
{
    public static class HttpRangeValidation
    {
        public static string? GetValidator(HttpResponseMessage response)
        {
            if (response.Headers.ETag is EntityTagHeaderValue etag && !etag.IsWeak)
                return "etag:" + etag.ToString();
            if (response.Content.Headers.LastModified is DateTimeOffset modified)
                return "date:" + modified.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);
            return null;
        }

        public static bool IsValidValidator(string? validator)
        {
            if (string.IsNullOrWhiteSpace(validator)) return false;
            if (validator.StartsWith("etag:", StringComparison.Ordinal) &&
                EntityTagHeaderValue.TryParse(validator.Substring(5), out var tag))
                return !tag.IsWeak;
            return validator.StartsWith("date:", StringComparison.Ordinal) &&
                DateTimeOffset.TryParseExact(validator.Substring(5), "R", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);
        }

        public static bool ApplyIfRange(HttpRequestMessage request, string? validator)
        {
            if (!IsValidValidator(validator)) return false;
            if (validator!.StartsWith("etag:", StringComparison.Ordinal))
            {
                EntityTagHeaderValue.TryParse(validator.Substring(5), out var tag);
                request.Headers.IfRange = new RangeConditionHeaderValue(tag!);
            }
            else
            {
                DateTimeOffset.TryParseExact(validator.Substring(5), "R", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var modified);
                request.Headers.IfRange = new RangeConditionHeaderValue(modified);
            }
            return true;
        }

        public static bool MatchesValidator(HttpResponseMessage response, string? validator)
        {
            if (!IsValidValidator(validator)) return false;
            if (validator!.StartsWith("etag:", StringComparison.Ordinal))
                return response.Headers.ETag == null ||
                    string.Equals(response.Headers.ETag.ToString(), validator.Substring(5), StringComparison.Ordinal);
            return response.Content.Headers.LastModified == null ||
                string.Equals(response.Content.Headers.LastModified.Value.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture),
                    validator.Substring(5), StringComparison.Ordinal);
        }

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
