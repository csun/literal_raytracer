using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LiteralRaytrace
{
    public class LiteralRaytraceOutputDisplay : MonoBehaviour
    {
        public RawImage Image;
        public LiteralRaytraceCamera Camera;

        // Update is called once per frame
        void Update()
        {
            if (Image.texture != Camera.Target)
            {
                Image.texture = Camera.Target;
                Image.SetNativeSize();
            }
        }
    }
}