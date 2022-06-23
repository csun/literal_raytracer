using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;

namespace LiteralRaytrace
{
    public class CameraRay
    {
        public Vector3 start;
        public Vector3 direction;
        public float startIntensity;
        public Color color;
        public int bounces = 0;
    }

    public class CachedMaterial
    {
        public Texture2D albedo;
    }

    public class LiteralRaytraceCamera : MonoBehaviour
    {
        static ProfilerMarker newRayPerfMarker = new ProfilerMarker("LiteralRaytrace.CreateLightRays");
        static ProfilerMarker processRayPerfMarker = new ProfilerMarker("LiteralRaytrace.ProcessQueuedRays");
        static ProfilerMarker updateTargetPerfMarker = new ProfilerMarker("LiteralRaytrace.UpdateTarget");

        public Material FullscreenPassMaterial;
        [HideInInspector]
        public RenderTexture Target;

        public int ActiveRayTarget = 100;
        public int MinBounces = 1;
        public int MaxBounces = 16;

        public float IntensityUpperBound = EVToIntensity(16);
        public float IntensityLowerBound = EVToIntensity(1);

        public Texture2D averageColor;
        private Texture2D samples;
        private float maxSamples;
        private Queue<CameraRay> rayQueue = new Queue<CameraRay>();
        private Dictionary<int, CachedMaterial> materialCache = new Dictionary<int, CachedMaterial>();

        private new Camera camera;

        private void Start()
        {
            camera = GetComponent<Camera>();
            camera.depthTextureMode = DepthTextureMode.Depth;
        }

        private void Update()
        {
            Init();

            CreateLightRays();
            ProcessQueuedRays();

            UpdateTarget();
        }
        private void Init()
        {
            if (Target == null || Target.width != Screen.width || Target.height != Screen.height)
            {
                if (Target != null)
                {
                    Target.Release();
                }

                Target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Target.enableRandomWrite = true;
                Target.Create();

                averageColor = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBAFloat, false, true);
                samples = new Texture2D(Screen.width, Screen.height, TextureFormat.RFloat, false, true);
                maxSamples = 1;  // Initialize to 1 to avoid divide by zero errors early on

                // Clear texture
                for (int x = 0; x < Target.width; x++)
                {
                    for (int y = 0; y < Target.height; y++)
                    {
                        averageColor.SetPixel(x, y, Color.black);
                        samples.SetPixel(x, y, Color.black);
                    }
                }
            }
        }

        private void CreateLightRays()
        {
            using (newRayPerfMarker.Auto())
            {
                var lights = FindObjectsOfType<Light>();
                foreach (var light in lights)
                {
                    for (var i = 0; i < ActiveRayTarget / lights.Length; i++)
                    {
                        var randRotation = Quaternion.AngleAxis(Random.Range(0, 360), light.transform.forward);
                        randRotation *= Quaternion.AngleAxis(Random.Range(0, light.spotAngle / 2), light.transform.up);

                        rayQueue.Enqueue(new CameraRay
                        {
                            start = light.transform.position,
                            direction = randRotation * light.transform.forward,
                            startIntensity = light.intensity,
                            color = light.color
                        });
                    }
                }
            }
        }

        private void ProcessQueuedRays()
        {
            using (processRayPerfMarker.Auto())
            {
                var raysToProcess = rayQueue;
                rayQueue = new Queue<CameraRay>();
                while (raysToProcess.Count > 0)
                {
                    var ray = raysToProcess.Dequeue();

                    var maxDistance = InvAttenuation(IntensityLowerBound / ray.startIntensity);
                    RaycastHit hitinfo;
                    var hit = Physics.Raycast(ray.start, ray.direction, out hitinfo, maxDistance);

                    // Set a ray end where we reach min intensity if nothing is hit
                    Vector3 rayEnd;
                    if (hit) { rayEnd = hitinfo.point; }
                    else { rayEnd = (maxDistance * ray.direction) + ray.start; }

                    // Draw the ray if it's past the min bounce threshold
                    if (ray.bounces >= MinBounces)
                    {
                        RasterizeRay(ray, rayEnd);
                    }

                    // Finally, queue a new ray if it will bounce
                    if (hit && ray.bounces < MaxBounces)
                    {
                        var newStartIntensity = Attenuation(hitinfo.distance) * ray.startIntensity;
                        var reflectedDirection = ray.direction - 2 * Vector3.Dot(ray.direction, hitinfo.normal) * hitinfo.normal;

                        var material = GetOrAddCachedMaterial(hitinfo);
                        var albedo = SampleTexture(material.albedo, hitinfo.textureCoord);

                        // TODO take normal map at point into account

                        rayQueue.Enqueue(new CameraRay
                        {
                            start = hitinfo.point,
                            direction = reflectedDirection,
                            startIntensity = newStartIntensity,
                            color = ray.color * albedo,
                            bounces = ray.bounces + 1
                        });
                    }
                }
            }
        }

        private void UpdateTarget()
        {
            using (updateTargetPerfMarker.Auto())
            {
                averageColor.Apply();
                samples.Apply();

                FullscreenPassMaterial.SetInt("_RayCount", 3000);

                /*
                DrawShader.SetTexture(0, "Result", Target);
                DrawShader.SetTexture(0, "_AverageColor", averageColor);
                DrawShader.SetTexture(0, "_Samples", samples);
                DrawShader.SetFloat("_MaxSamples", maxSamples);

                DrawShader.Dispatch(0, Mathf.CeilToInt(Screen.width / 8.0f), Mathf.CeilToInt(Screen.height / 8.0f), 1);
                */
            }
        }

        private Color SampleTexture(Texture2D tex, Vector2 uv)
        {
            uv.x *= tex.width;
            uv.y *= tex.height;
            return tex.GetPixel(Mathf.FloorToInt(uv.x), Mathf.FloorToInt(uv.y));
        }

        private CachedMaterial GetOrAddCachedMaterial(RaycastHit hitinfo)
        {
            var gameObject = hitinfo.collider.gameObject;
            var id = gameObject.GetInstanceID();
            if (!materialCache.ContainsKey(id))
            {
                var material = gameObject.GetComponent<Renderer>().material;
                Assert.AreEqual(material.shader.name, "HDRP/Lit");

                materialCache[id] = new CachedMaterial
                {
                    albedo = (Texture2D)material.GetTexture("_BaseColorMap")
                };
            }

            return materialCache[id];
        }

        private void RasterizeRay(CameraRay ray, Vector3 rayEnd)
        {
            var screenStart = camera.WorldToScreenPoint(ray.start);
            var screenEnd = camera.WorldToScreenPoint(rayEnd);
            var screenDelta = screenEnd - screenStart;

            var startRatio = FindScreenVisibleRatio(screenStart, screenDelta);
            var endRatio = FindScreenVisibleRatio(screenEnd, -screenDelta);

            if (startRatio > 1 || startRatio < 0 || endRatio > 1 || endRatio < 0)
            {
                return;
            }

            screenStart += screenDelta * startRatio;
            screenEnd -= screenDelta * endRatio;
            var totalWorldDistance = (ray.start - rayEnd).magnitude;
            DrawLine(new Vector2Int(Mathf.FloorToInt(screenStart.x), Mathf.FloorToInt(screenStart.y)),
                new Vector2Int(Mathf.FloorToInt(screenEnd.x), Mathf.FloorToInt(screenEnd.y)),
                ray.color,
                ray.startIntensity,
                startRatio * totalWorldDistance,
                totalWorldDistance * (1 - (startRatio + endRatio)));

            // TODO eventually add depth checks
        }

        private bool ScreenPointInBounds(Vector3 point)
        {
            return point.x >= 0 && point.x < Target.width &&
                point.y >= 0 && point.y < Target.height &&
                point.z >= 0;
        }

        // Find the first section of a screenspace line where it becomes visible, expressed as a ratio
        // of the line segment before to the line segment after this point.
        private float FindScreenVisibleRatio(Vector3 start, Vector3 delta)
        {
            // If our point is already in bounds, we don't need to move at all, so the ratio is 0
            if (ScreenPointInBounds(start))
            {
                return 0;
            }

            var minCandidate = Mathf.Infinity;

            void UpdateCandidateIfInBounds(float ratio)
            {
                if (ratio >= 0 &&
                    ratio <= 1 &&
                    ScreenPointInBounds(start + (ratio * delta)) &&
                    ratio < minCandidate)
                {
                    minCandidate = ratio;
                }
            }

            // Screen coordinate system starts in lower left with positive x going right and positive y going up

            // left of frame
            if (start.x < 0 && delta.x > 0)
            {
                UpdateCandidateIfInBounds(-start.x / delta.x);
            }
            // right of frame
            else if (start.x >= Target.width && delta.x < 0)
            {
                // Add an extra -1 so that it ends up at the pixel index width - 1
                UpdateCandidateIfInBounds((Target.width - start.x - 1) / delta.x);
            }

            // below frame
            if (start.y < 0 && delta.y > 0)
            {
                UpdateCandidateIfInBounds(-start.y / delta.y);
            }
            // above frame
            else if (start.y >= Target.height && delta.y < 0)
            {
                // Add an extra -1 so that it ends up at the pixel index height - 1
                UpdateCandidateIfInBounds((Target.height - start.y - 1) / delta.y);
            }

            // behind camera
            if (start.z < 0)
            {
                UpdateCandidateIfInBounds(-start.z / delta.z);
            }

            // Return the lowest ratio that gives us a point on the line that's in bounds
            return minCandidate;
        }

        // Adapted from this gist https://gist.github.com/Pyr3z/46884d67641094d6cf353358566db566
        private void DrawLine(Vector2Int start, Vector2Int end, Color baseColor, float sourceIntensity, float initialWorldDistance, float totalWorldDistance)
        {
            var delta = new Vector2Int(System.Math.Abs(end.x - start.x), System.Math.Abs(end.y - start.y));
            var increment = new Vector2Int((end.x < start.x) ? -1 : 1, (end.y < start.y) ? -1 : 1);
            var initialScreenDistance = delta.magnitude;

            float baseColorHue, baseColorSat, baseColorVal;
            Color.RGBToHSV(baseColor, out baseColorHue, out baseColorSat, out baseColorVal);

            Color GetColor(float screenDistance)
            {
                var intensity = NormalizeIntensity(
                    sourceIntensity * Attenuation(initialWorldDistance + ((1 - (screenDistance / initialScreenDistance)) * totalWorldDistance)));

                return Color.HSVToRGB(
                    baseColorHue,
                    baseColorSat,
                    baseColorVal * intensity);
            }

            int i = delta.x + delta.y;
            int error = delta.x - delta.y;
            delta.x *= 2;
            delta.y *= 2;

            while (i-- > 0)
            {
                RecordSample(start.x, start.y, GetColor((start - end).magnitude));

                if (error < 0)
                {
                    start.y += increment.y;
                    error += delta.x;
                }
                else
                {
                    start.x += increment.x;
                    error -= delta.y;
                }
            }

            RecordSample(end.x, end.y, GetColor(0));
        }

        private void RecordSample(int x, int y, Color color)
        {
            var count = samples.GetPixel(x, y).r + 1;
            samples.SetPixel(x, y, new Color(count, 0, 0));
            maxSamples = count > maxSamples ? count : maxSamples;

            if (count > 1)
            {
                // Average the color in-place
                var ratio = (count - 1.0f) / count;
                color = (ratio * averageColor.GetPixel(x, y)) + ((1 - ratio) * color);
            }

            averageColor.SetPixel(x, y, color);
        }

        private float NormalizeIntensity(float intensity)
        {
            return Mathf.Clamp((intensity - IntensityLowerBound) / (IntensityUpperBound - IntensityLowerBound), 0, 1);
        }

        // Explanation here https://gamedev.stackexchange.com/questions/131372/light-attenuation-formula-derivation
        private static float Attenuation(float distance)
        {
            return 1.0f / (1.0f + distance * distance);
        }

        // Gets the distance required to create a given attenuation value
        private static float InvAttenuation(float attenuation)
        {
            return Mathf.Sqrt((1.0f / attenuation) - 1.0f);
        }

        private static float EVToIntensity(float ev)
        {
            // In HDRP, it appears that intensity 1 == EV 3. We want to match that.
            return Mathf.Pow(2, ev - 3);
        }
    }
}
