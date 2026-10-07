using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// A grey cloud layer over the sky in overcast weather, storms and fog. The procedural sky can't do cloud (thick,
    /// it turns yellow), so a big dome centred on the camera fades in over it. It's far enough away that the fog
    /// colours it completely, so it meets the horizon seamlessly, darkens at night with the fog, and lights up with
    /// each lightning flash.
    /// </summary>
    public class CloudDome : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [Tooltip("Transparent, fogged, unlit material: the fog decides the dome's colour.")]
        [SerializeField] Material material;
        [Tooltip("Inside the camera's far clip, but far enough for fog to cover it fully.")]
        [SerializeField] float radius = 2500f;
        [Tooltip("Cloud cover at which the sky starts and finishes being hidden.")]
        [SerializeField] Vector2 coverRange = new(0.3f, 0.9f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Transform dome;
        Material instance;
        Camera view;

        void Start()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Cloud Dome";
            Destroy(sphere.GetComponent<Collider>());
            dome = sphere.transform;
            dome.SetParent(transform, false);
            dome.localScale = Vector3.one * radius * 2f;
            var renderer = sphere.GetComponent<MeshRenderer>();
            instance = new Material(material);
            renderer.sharedMaterial = instance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (view == null)
                view = Camera.main;
            if (view == null || dome == null)
                return;
            dome.position = view.transform.position;
            float cover = Mathf.Max(timeOfDay.Overcast, timeOfDay.WeatherFog);
            float alpha = Mathf.SmoothStep(0f, 0.97f, Mathf.InverseLerp(coverRange.x, coverRange.y, cover));
            instance.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
            dome.gameObject.SetActive(alpha > 0.005f);
        }

        void OnDestroy()
        {
            if (instance != null)
                Destroy(instance);
        }
    }
}
