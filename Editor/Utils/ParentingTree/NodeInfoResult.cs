using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace marble810.AvatarTools.Utils
{
    public class NodeInfoResult
    {
        private List<TextSegment> segments = new List<TextSegment>();
        public IReadOnlyList<TextSegment> Segments => segments;

        public bool IsEmpty => segments.Count == 0;
        public int Count => segments.Count;

        public NodeInfoResult Add(TextSegment segment)
        {
            if (!segment.IsEmpty())
            {
                segments.Add(segment);
            }
            return this;
        }

        public NodeInfoResult Space()
        {
            segments.Add(TextSegment.White(" "));
            return this;
        }

        public static NodeInfoResult Create() => new NodeInfoResult();
        public static NodeInfoResult Empty = new NodeInfoResult();

        public override string ToString()
        {
            if (IsEmpty) return "";
            var parts = new string[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                parts[i] = segments[i].ToString();
            }
            return string.Join("", parts);
        }


    }
}