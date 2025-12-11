using UnityEngine;
using UnityEditor;

namespace marble810.AvatarTools.Utils
{
    public struct TextSegment
    {
        public string text;
        public Color color;

        public TextSegment(string text, Color color)
        {
            this.text = text;
            this.color = color;
        }

        public static TextSegment White(string text)
        {
            return new TextSegment(text, Color.white);
        }

        public static TextSegment Red(string text)
        {
            return new TextSegment(text, Color.red);
        }

        public static TextSegment Green(string text)
        {
            return new TextSegment(text, Color.green);
        }

        //Utils
        public readonly bool IsEmpty()
        {
            return string.IsNullOrEmpty(text);
        }
        
        public readonly bool IsSpace()
        {
            return text == " ";
        }

        public override string ToString()
        {
            return text;
        }

    }
}