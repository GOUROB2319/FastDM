using FastDM;
using System.Net;
using System.Net.Http.Headers;

namespace FastDM.Tests
{
    static class HttpRangeValidationTests
    {
        static HttpResponseMessage Partial(long from, long to, long length, long bodyLength)
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
            response.Content = new ByteArrayContent(new byte[checked((int)bodyLength)]);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, length);
            response.Content.Headers.ContentLength = bodyLength;
            return response;
        }

        public static void Run()
        {
            T.Section("HTTP range response integrity");
            using (var probe = Partial(0, 0, 100, 1))
                T.Check("probe accepts exactly byte zero and a known resource size", HttpRangeValidation.IsProbeResponse(probe, out long total) && total == 100);

            using (var badProbe = Partial(1, 1, 100, 1))
            {
                bool rejected = false;
                try { HttpRangeValidation.IsProbeResponse(badProbe, out _); }
                catch (InvalidDataException) { rejected = true; }
                T.Check("probe rejects a mismatched Content-Range", rejected);
            }

            using (var range = Partial(10, 19, 100, 10))
                T.Check("download accepts a matching segment response", HttpRangeValidation.ValidateSegmentResponse(range, 10, 19, 100) == 10);

            using (var wrongSegment = Partial(0, 9, 100, 10))
            {
                bool rejected = false;
                try { HttpRangeValidation.ValidateSegmentResponse(wrongSegment, 10, 19, 100); }
                catch (InvalidDataException) { rejected = true; }
                T.Check("download rejects a different segment offset", rejected);
            }

            using (var wrongLength = Partial(10, 19, 100, 9))
            {
                bool rejected = false;
                try { HttpRangeValidation.ValidateSegmentResponse(wrongLength, 10, 19, 100); }
                catch (InvalidDataException) { rejected = true; }
                T.Check("download rejects a body length that disagrees with the requested range", rejected);
            }

            using (var etagResponse = new HttpResponseMessage(HttpStatusCode.OK))
            {
                etagResponse.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
                string? validator = HttpRangeValidation.GetValidator(etagResponse);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/file");
                T.Check("strong ETag is captured and sent as If-Range",
                    validator == "etag:\"stable\"" && HttpRangeValidation.ApplyIfRange(request, validator) &&
                    request.Headers.IfRange?.EntityTag?.ToString() == "\"stable\"");
            }

            using (var weakEtag = new HttpResponseMessage(HttpStatusCode.OK))
            {
                weakEtag.Headers.ETag = new EntityTagHeaderValue("\"weak\"", isWeak: true);
                T.Check("weak ETag cannot guard resumable byte ranges", HttpRangeValidation.GetValidator(weakEtag) == null);
            }

            using (var dateResponse = new HttpResponseMessage(HttpStatusCode.OK))
            {
                dateResponse.Content = new ByteArrayContent(Array.Empty<byte>());
                dateResponse.Content.Headers.LastModified = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
                string? validator = HttpRangeValidation.GetValidator(dateResponse);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/file");
                T.Check("Last-Modified is captured and sent as date If-Range",
                    validator == "date:Thu, 02 Jan 2025 03:04:05 GMT" && HttpRangeValidation.ApplyIfRange(request, validator) &&
                    request.Headers.IfRange?.Date == dateResponse.Content.Headers.LastModified);
            }

            using (var changed = new HttpResponseMessage(HttpStatusCode.PartialContent))
            {
                changed.Headers.ETag = new EntityTagHeaderValue("\"changed\"");
                T.Check("a changed response ETag invalidates a resumed range",
                    !HttpRangeValidation.MatchesValidator(changed, "etag:\"stable\""));
            }
        }
    }
}
