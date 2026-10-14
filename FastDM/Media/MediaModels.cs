using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace FastDM
{
    public enum MediaKind { None, Direct, Hls, Dash, Page }

    // ====================== মডেল ======================
    // ডাউনলোড আইটেমে সেভ হয় (state.json-এ)। সেগমেন্ট লিস্ট সেভ হয় না, প্রতিবার ম্যানিফেস্ট থেকে আবার পড়া হয়।
    public class StreamSpec
    {
        public string Kind { get; set; } = "hls";      // "hls" | "dash"
        public string? ManifestUrl { get; set; }         // DASH: .mpd লিঙ্ক
        public string? VideoUrl { get; set; }            // HLS: বাছাই করা মিডিয়া প্লেলিস্ট
        public string? AudioUrl { get; set; }            // HLS: আলাদা অডিও প্লেলিস্ট (থাকলে)
        public string? VideoRepId { get; set; }          // DASH
        public string? AudioRepId { get; set; }          // DASH
        public bool AudioOnly { get; set; }
        public string? Referer { get; set; }
    }

    public class VideoOpt
    {
        public string Label = ""; public string? Url, RepId, AudioGroup;
        public int Height;
        public long Bandwidth;
    }

    public class AudioOpt
    {
        public string Label = ""; public string? Url, RepId, Group;
        public long Bandwidth;
    }

    public class MediaOptions
    {
        public string Kind = "hls";                 // "hls" | "dash"
        public string ManifestUrl = "";
        public string? Referer;
        public string? Title;
        public List<VideoOpt> Videos = new List<VideoOpt>();
        public List<AudioOpt> Audios = new List<AudioOpt>();
    }

    public class MediaCandidate
    {
        public string Url = "";
        public MediaKind Kind;
        public string Label = "";
        public override string ToString() => Label;
    }

    public class MediaProbeResult
    {
        public MediaKind Kind;
        public string Url = "";
        public string? Title;
        public List<MediaCandidate> Candidates = new List<MediaCandidate>();
    }
}
