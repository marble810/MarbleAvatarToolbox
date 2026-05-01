using UnityEngine.UIElements;

namespace marble810.MarbleAvatarTools.CustomGUI
{
    internal class Separator : VisualElement
    {
        internal Separator()
        {
            // var ve = new VisualElement();
            this.style.height = 1f;
            this.style.backgroundColor = Helper.ColorPreset.Gray;
            this.style.marginTop = 5f;
            this.style.marginBottom = 5f;
            this.style.marginLeft = 2f;
            this.style.marginRight = 2f;
        }
    }
}