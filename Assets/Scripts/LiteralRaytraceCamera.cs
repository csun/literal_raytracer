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
        public float startIntensity;
        public Color color;
        public int bounces = 0;
    }

    public class DrawRay
    {
        public Vector3 screenspaceStart;
        public Vector3 screenspaceDelta;
        public float worldspaceLength;
        public float normalizedStartIntensity;
        public Color color;
    }

    public class CachedMaterial
    {
        public Texture2D albedo;
    }

    public class LiteralRaytraceCamera : MonoBehaviour
    {
        static ProfilerMarker newRayPerfMarker = new ProfilerMarker("LiteralRaytrace.CreateLightRays");
        static ProfilerMarker processRayPerfMarker = new ProfilerMarker("LiteralRaytrace.ProcessQueuedRays");

        public int ActiveRayTarget = 100;
        public int MinBounces = 1;
        public int MaxBounces = 16;
        [HideInInspector]
        public List<DrawRay> RaysToDraw = new List<DrawRay>();

        public float IntensityUpperBound = EVToIntensity(16);
        public float IntensityLowerBound = EVToIntensity(1);

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

                    var randRotation = Quaternion.AngleAxis(Random.Range(0, 360), light.transform.forward);
                    randRotation *= Quaternion.AngleAxis(Random.Range(0, light.spotAngle / 2), light.transform.up);

                    castQueue.Enqueue(new CameraRay
                    {
                        start = light.transform.position,
                        direction = randRotation * light.transform.forward,
                        startIntensity = light.intensity,
                        color = light.color
                    });
                }
            }
        }

        private void ProcessQueuedRays()
        {
            using (processRayPerfMarker.Auto())
            {
                var raysToProcess = castQueue.Count;
                RaysToDraw.Clear();

                for (var i = 0; i < raysToProcess; i++)
                {
                    var ray = castQueue.Dequeue();

                    var maxDistance = InvAttenuation(IntensityLowerBound / ray.startIntensity);
                    RaycastHit hitinfo;
                    var hit = Physics.Raycast(ray.start, ray.direction, out hitinfo, maxDistance);

                    // Set a ray end where we reach min intensity if nothing is hit
                    Vector3 rayEnd;
                    if (hit) { rayEnd = hitinfo.point; }
                    else { rayEnd = (maxDistance * ray.direction) + ray.start; }

                    Debug.DrawRay(ray.start, rayEnd - ray.start, Color.yellow);
                    // Draw the ray if it's past the min bounce threshold
                    if (ray.bounces >= MinBounces)
                    {
                        var screenspaceStart = WorldToScreen(ray.start);
                        var screenspaceEnd = WorldToScreen(rayEnd);
                        var screenspaceDelta = screenspaceEnd - screenspaceStart;

                        RaysToDraw.Add(new DrawRay
                        {
                            screenspaceStart = screenspaceStart,
                            screenspaceDelta = screenspaceDelta,
                            worldspaceLength = (rayEnd - ray.start).magnitude,
                            normalizedStartIntensity = NormalizeIntensity(ray.startIntensity),
                            color = ray.color
                        });
                    }

                    // Finally, queue a new ray if it will bounce
                    if (hit && ray.bounces < MaxBounces)
                    {
                        var newStartIntensity = Attenuation(hitinfo.distance) * ray.startIntensity;

                        var material = GetOrAddCachedMaterial(hitinfo);
                        var albedo = SampleTexture(material.albedo, hitinfo.textureCoord);

                        // TODO take normal map at point into account

                        castQueue.Enqueue(new CameraRay
                        {
                            start = hitinfo.point,
                            direction = Vector3.Reflect(ray.direction, hitinfo.normal),
                            startIntensity = newStartIntensity,
                            color = ray.color * albedo,
                            bounces = ray.bounces + 1
                        });
                    }
                }
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

        private Vector3 WorldToScreen(Vector3 world)
        {
            var screen = camera.WorldToScreenPoint(world);
            if (screen.z < 0)
            {
                var center = new Vector3(Screen.width / 2, Screen.height / 2, 0);
                var diff = center - screen;
                screen.x += 2 * diff.x;
                screen.y += 2 * diff.y;
            }
            screen.z = InverseLinearEyeDepth(screen.z);

            return screen;
        }

        // The inverse of shader function LinearEyeDepth()
        private float InverseLinearEyeDepth(float linDepth)
        {
            var sign = 1;
            if (linDepth < 0)
            {
                linDepth = -linDepth;
                sign = -1;
            }

            var near = camera.nearClipPlane;
            var far = camera.farClipPlane;
            return sign * ((1.0f / linDepth) - (1.0f / near)) / ((1.0f / far) - (1.0f / near));
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
