using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;

namespace LiteralRaytrace
{
    public class CameraRay
    {
        public Vector3 start;
        public Vector3 direction;
        public Color color;
        public int bounces = 0;
    }

    public class DrawRay
    {
        public Vector3 screenspaceStart;
        public Vector3 screenspaceDelta;
        public Color color;
    }

    public class CachedMaterial
    {
        Texture2D baseMap;
        Texture2D maskMap;
        Color baseColor;
        float noMapSmoothness;
        float noMapMetallic;
        Vector2 mapSmoothnessRange;
        Vector2 mapMetallicRange;

        public CachedMaterial(Material material)
        {
            baseMap = (Texture2D)material.GetTexture("_BaseColorMap");
            maskMap = (Texture2D)material.GetTexture("_MaskMap");
            baseColor = material.GetColor("_BaseColor");
            noMapSmoothness = material.GetFloat("_Smoothness");
            noMapMetallic = material.GetFloat("_Metallic");
            mapSmoothnessRange = new Vector2(
                material.GetFloat("_SmoothnessRemapMin"),
                material.GetFloat("_SmoothnessRemapMax"));
            mapMetallicRange = new Vector2(
                material.GetFloat("_MetallicRemapMin"),
                material.GetFloat("_MetallicRemapMax"));
        }

        public Color SampleBaseColor(Vector2 uv)
        {
            return SampleTexture(baseMap, uv) * baseColor;
        }

        public float SampleMetallic(Vector2 uv)
        {
            if (maskMap == null)
            {
                return noMapMetallic;
            }
            var sample = SampleTexture(maskMap, uv).r;
            return (mapMetallicRange.y - mapMetallicRange.x) * sample + mapMetallicRange.x;
        }

        public float SampleSmoothness(Vector2 uv)
        {
            if (maskMap == null)
            {
                return noMapSmoothness;
            }
            var sample = SampleTexture(maskMap, uv).a;
            return (mapSmoothnessRange.y - mapSmoothnessRange.x) * sample + mapSmoothnessRange.x;
        }

        private Color SampleTexture(Texture2D tex, Vector2 uv)
        {
            if (tex == null)
            {
                return Color.white;
            }

            uv.x *= tex.width;
            uv.y *= tex.height;
            return tex.GetPixel(Mathf.FloorToInt(uv.x), Mathf.FloorToInt(uv.y));
        }
    }

    public class LiteralRaytraceCamera : MonoBehaviour
    {
        static ProfilerMarker newRayPerfMarker = new ProfilerMarker("LiteralRaytrace.CreateLightRays");
        static ProfilerMarker processRayPerfMarker = new ProfilerMarker("LiteralRaytrace.ProcessQueuedRays");

        public int ActiveRayTarget = 100;
        public int MinBounces = 1;
        public int MaxBounces = 16;
        public float MinBrightness = 0.0001f;
        public float MaxRayDistance = 1000;
        [HideInInspector]
        public List<DrawRay> RaysToDraw = new List<DrawRay>();

        public float IntensityUpperBound = EVToIntensity(16);

        private Queue<CameraRay> castQueue = new Queue<CameraRay>();
        private Dictionary<int, CachedMaterial> materialCache = new Dictionary<int, CachedMaterial>();

        private Light[] lights;
        private int nextLight = 0;
        private new Camera camera;

        private void Start()
        {
            camera = GetComponent<Camera>();
            camera.depthTextureMode = DepthTextureMode.Depth;
            lights = FindObjectsOfType<LiteralRaytraceLight>()
                .Where(l => l.gameObject.activeInHierarchy)
                .Select(l => l.GetComponent<Light>())
                .ToArray();
        }

        private void Update()
        {
            CreateLightRays();
            ProcessQueuedRays();
        }

        private void CreateLightRays()
        {
            using (newRayPerfMarker.Auto())
            {
                for (var i = 0; i < ActiveRayTarget - castQueue.Count; i++)
                {
                    var light = lights[nextLight];
                    nextLight = (nextLight + 1) % lights.Length;

                    var direction = RandomConeDirectionNormal(
                        light.spotAngle, light.spotAngle / 6, light.transform.forward);

                    castQueue.Enqueue(new CameraRay
                    {
                        start = light.transform.position,
                        direction = direction,
                        color = light.color * NormalizeIntensity(light.intensity)
                    });
                }
            }
        }

        private void ProcessQueuedRays()
        {
            using (processRayPerfMarker.Auto())
            {
                var raysToProcess = Mathf.Min(ActiveRayTarget, castQueue.Count);
                RaysToDraw.Clear();

                for (var i = 0; i < raysToProcess; i++)
                {
                    var ray = castQueue.Dequeue();

                    RaycastHit hitinfo;
                    var hit = Physics.Raycast(ray.start, ray.direction, out hitinfo, MaxRayDistance);

                    // Set a ray end where we reach min intensity if nothing is hit
                    Vector3 rayEnd;
                    if (hit) { rayEnd = hitinfo.point; }
                    else { rayEnd = (MaxRayDistance * ray.direction) + ray.start; }

                    // Draw the ray if it's past the min bounce threshold
                    if (ray.bounces >= MinBounces)
                    {
                        AddDrawRay(ray, rayEnd);
                    }

                    // Finally, queue a new ray if it will bounce
                    if (hit && ray.bounces < MaxBounces)
                    {
                        var material = GetOrAddCachedMaterial(hitinfo);
                        var baseColor = material.SampleBaseColor(hitinfo.textureCoord);
                        var metallicRatio = material.SampleMetallic(hitinfo.textureCoord);

                        // We use smoothness to determine the sigma of the random noise applied to the direction
                        // that the reflected ray will bounce. This constant multiplier is arbitrarily chosen
                        var smoothnessSigma = (1 - material.SampleSmoothness(hitinfo.textureCoord)) * 40;

                        var randomizedNormal = RandomConeDirectionNormal(180, smoothnessSigma, hitinfo.normal);
                        var reflectedRay = Vector3.Reflect(ray.direction, randomizedNormal);

                        // F0 is fresnel reflectance ratio at 0 degrees. The defaults here are estimates for dielectric and metallic surfaces.
                        // See https://substance3d.adobe.com/tutorials/courses/the-pbr-guide-part-1
                        var effectiveF0 = Mathf.Lerp(0.04f, 0.8f, metallicRatio);

                        // Fresnel equation
                        var reflectedAmt = effectiveF0 + (1 - effectiveF0) * Mathf.Pow(1 - Vector3.Dot(randomizedNormal, reflectedRay), 5);
                        // Metallic materials absorb light, they don't refract / scatter
                        var refractedAmt = (1 - reflectedAmt) * (1 - metallicRatio);

                        // Specular / Reflected Ray
                        if (Vector3.Dot(reflectedRay, hitinfo.normal) > 0)
                        {
                            // Dielectric materials have white specular, metallic have baseColor specular
                            var reflectColor = Color.Lerp(Color.white, baseColor, metallicRatio) * reflectedAmt * ray.color;
                            CastIfBrightEnough(new CameraRay
                            {
                                start = hitinfo.point,
                                direction = reflectedRay,
                                color = reflectColor,
                                bounces = ray.bounces + 1
                            });
                        }

                        // Diffuse / Refracted Ray
                        if (refractedAmt > 0)
                        {
                            var refractColor = baseColor * refractedAmt * ray.color;
                            CastIfBrightEnough(new CameraRay
                            {
                                start = hitinfo.point,
                                direction = RandomConeDirectionUniform(180, hitinfo.normal),  // Scatter anywhere uniformly
                                color = refractColor,
                                bounces = ray.bounces + 1
                            });
                        }
                    }
                }
            }
        }

        private void AddDrawRay(CameraRay ray, Vector3 rayEnd)
        {
            // Screenspace math starts to get really weird if we're drawing a line from a point behind
            // the camera. Make sure that we just shorten the ray to where it crosses the xy plane rather than
            // letting it do this.
            var croppedRayStart = CropRayStartToPositiveCameraZ(ray.start, rayEnd);
            var croppedRayEnd = CropRayStartToPositiveCameraZ(rayEnd, ray.start);

            if (croppedRayStart.HasValue && croppedRayEnd.HasValue)
            {
                var screenspaceStart = WorldToScreen(croppedRayStart.Value);
                var screenspaceEnd = WorldToScreen(croppedRayEnd.Value);
                var screenspaceDelta = screenspaceEnd - screenspaceStart;

                RaysToDraw.Add(new DrawRay
                {
                    screenspaceStart = screenspaceStart,
                    screenspaceDelta = screenspaceDelta,
                    color = ray.color
                });
            }
        }

        private void CastIfBrightEnough(CameraRay ray)
        {
            if (ray.color.grayscale > MinBrightness)
            {
                castQueue.Enqueue(ray);
            }
        }

        private Vector3? CropRayStartToPositiveCameraZ(Vector3 start, Vector3 end)
        {
            var localStart = camera.transform.InverseTransformPoint(start);
            var localEnd = camera.transform.InverseTransformPoint(end);
            if (localStart.z > 0) { return start; }
            else if (localStart.z < 0 && localEnd.z < 0) { return null; }

            var delta = localEnd - localStart;
            var ratio = -localStart.z / delta.z;

            return camera.transform.TransformPoint(localStart + (ratio * delta));
        }

        private CachedMaterial GetOrAddCachedMaterial(RaycastHit hitinfo)
        {
            var gameObject = hitinfo.collider.gameObject;

            var id = gameObject.GetInstanceID();
            if (!materialCache.ContainsKey(id))
            {
                var material = gameObject.GetComponent<Renderer>().material;
                Assert.AreEqual(material.shader.name, "HDRP/Lit");

                materialCache[id] = new CachedMaterial(material);
            }

            return materialCache[id];
        }

        private Vector3 WorldToScreen(Vector3 world)
        {
            var screen = camera.WorldToScreenPoint(world);
            if (screen.z < 0)
            {
                // When a point behind the camera is projected onto the screen, its
                // screenspace xy position is mirrored about the center of the screen.
                // Correct that.
                var center = new Vector3(Screen.width / 2, Screen.height / 2, 0);
                var diff = center - screen;
                screen.x += 2 * diff.x;
                screen.y += 2 * diff.y;
            }

            // Converting to nonlinear depth allows us to calculate depth linearly as we move along
            // the line in screenspace, which is what we do in the sampling shader.
            screen.z = InverseLinearEyeDepth(screen.z);

            return screen;
        }

        // The inverse of shader function LinearEyeDepth()
        private float InverseLinearEyeDepth(float linDepth)
        {
            if (linDepth < 0)
            {
                // When dealing with points behind the camera, this inverse linear depth function
                // starts to get messed up. To deal with that, just return 0. We correct for
                // points extending behind the camera in worldspace so this should not cause much error.
                return 0;
            }

            var near = camera.nearClipPlane;
            var far = camera.farClipPlane;
            return ((1.0f / linDepth) - (1.0f / near)) / ((1.0f / far) - (1.0f / near));
        }

        private float NormalizeIntensity(float intensity)
        {
            return Mathf.Clamp(intensity / IntensityUpperBound, 0, 1);
        }

        private static float EVToIntensity(float ev)
        {
            // In HDRP, it appears that intensity 1 == EV 3. We want to match that.
            return Mathf.Pow(2, ev - 3);
        }

        private Vector3 RandomConeDirectionNormal(float maxAngle, float sigma, Vector3 origDirection)
        {
            var origRotation = Quaternion.FromToRotation(Vector3.forward, origDirection);
            return origRotation *
                Quaternion.AngleAxis(Random.Range(0, 360), Vector3.forward) *
                Quaternion.AngleAxis(RandomGaussian(sigma, 0, -maxAngle / 2, maxAngle / 2), Vector3.up) *
                Vector3.forward;
        }

        private Vector3 RandomConeDirectionUniform(float maxAngle, Vector3 origDirection)
        {
            var origRotation = Quaternion.FromToRotation(Vector3.forward, origDirection);
            return origRotation *
                Quaternion.AngleAxis(Random.Range(0, 360), Vector3.forward) *
                Quaternion.AngleAxis(Random.Range(-maxAngle / 2, maxAngle / 2), Vector3.up) *
                Vector3.forward;
        }

        // From https://github.com/VoxusSoftware/unity-random/blob/master/Assets/Voxus/Random/RandomGaussian.cs
        private static float RandomGaussian(float sigma, float mu, float min = float.MinValue, float max = float.MaxValue)
        {
            float x1, x2, w, y1, candidate;

            do
            {
                do
                {
                    x1 = 2f * Random.value - 1f;
                    x2 = 2f * Random.value - 1f;
                    w = x1 * x1 + x2 * x2;
                } while (w >= 1f);

                w = Mathf.Sqrt((-2f * Mathf.Log(w)) / w);
                y1 = x1 * w;

                candidate = (y1 * sigma) + mu;

            } while (candidate < min || candidate > max);

            return candidate;
        }
    }
}
