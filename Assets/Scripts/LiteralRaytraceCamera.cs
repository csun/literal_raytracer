using System.Collections.Generic;
using UnityEngine;

namespace LiteralRaytrace
{
    public class CameraRay
    {
        public Vector3 start;
        public Vector3 direction;
        public float startIntensity;
        public int bounces = 0;
    }

    public class LiteralRaytraceCamera : MonoBehaviour
    {
        [HideInInspector]
        public Texture2D Target;

        public int ActiveRayTarget = 100;
        public int MinBounces = 1;
        public int MaxBounces = 16;

        public float IntensityUpperBound = EVToIntensity(16);
        public float IntensityLowerBound = EVToIntensity(1);

        private Queue<CameraRay> rayQueue = new Queue<CameraRay>();

        private Camera camera;

        private void Start()
        {
            camera = GetComponent<Camera>();
        }

        private void Update()
        {
            InitTexture();

            // Generate new rays
            var lights = FindObjectsOfType<Light>();
            foreach (var light in lights)
            {
                for (var i = 0; i < ActiveRayTarget / lights.Length; i++)
                {
                    var randRotation = Quaternion.Euler(
                        Random.Range(-light.spotAngle, light.spotAngle),
                        Random.Range(-light.spotAngle, light.spotAngle),
                        0);

                    // queue new rays
                    rayQueue.Enqueue(new CameraRay
                    {
                        start = light.transform.position,
                        direction = randRotation * light.transform.forward,
                        startIntensity = light.intensity
                    });
                }
            }

            // Process queued rays
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

                    // TODO take normal map at point into account

                    rayQueue.Enqueue(new CameraRay
                    {
                        start = hitinfo.point,
                        direction = reflectedDirection,
                        startIntensity = newStartIntensity,
                        bounces = ray.bounces + 1
                    });
                }
            }

            Target.Apply();
        }

        private void InitTexture()
        {
            if (Target == null || Target.width != Screen.width || Target.height != Screen.height)
            {
                Target = new Texture2D(Screen.width, Screen.height);

                // Clear texture
                for (int x = 0; x < Target.width; x++)
                {
                    for (int y = 0; y < Target.height; y++)
                    {
                        Target.SetPixel(x, y, Color.black);
                    }
                }
            }
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
                Color.white,
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

            // left of frame
            if (start.x < 0 && delta.x > 0)
            {
                UpdateCandidateIfInBounds(-start.x / delta.x);
            }
            // right of frame
            else if (start.x >= Target.width && delta.x < 0)
            {
                UpdateCandidateIfInBounds((Target.width - start.x) / delta.x);
            }

            // above frame
            if (start.y < 0 && delta.y > 0)
            {
                UpdateCandidateIfInBounds(-start.y / delta.y);
            }
            // below frame
            else if (start.y >= Target.height && delta.y < 0)
            {
                UpdateCandidateIfInBounds((Target.height - start.y) / delta.y);
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

            Color GetColor(float screenDistance)
            {
                return baseColor * NormalizeIntensity(
                    sourceIntensity * Attenuation(initialWorldDistance + ((1 - (screenDistance / initialScreenDistance)) * totalWorldDistance)));
            }

            // Handle perfect diagonals
            if (delta.x == delta.y)
            {
                while (delta.x-- > 0)
                {
                    delta.y--;

                    Target.SetPixel(start.x, start.y, GetColor(delta.magnitude));

                    start.x += increment.x;
                    start.y += increment.y;
                }

            }
            // Handle all other lines
            else
            {
                int i = delta.x + delta.y;
                int error = delta.x - delta.y;
                delta.x *= 2;
                delta.y *= 2;

                while (i-- > 0)
                {
                    Target.SetPixel(start.x, start.y, GetColor((start - end).magnitude));

                    if (error < 0)
                    {
                        if (error >= -delta.x) // new diagonal case
                        {
                            start.x += increment.x;
                            error -= delta.y;
                            --i;
                        }

                        start.y += increment.y;
                        error += delta.x;
                    }
                    else
                    {
                        if (error > delta.y) // new diagonal case
                        {
                            start.y += increment.y;
                            error += delta.x;
                            --i;
                        }

                        start.x += increment.x;
                        error -= delta.y;
                    }
                }
            }

            Target.SetPixel(end.x, end.y, GetColor(0));
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
