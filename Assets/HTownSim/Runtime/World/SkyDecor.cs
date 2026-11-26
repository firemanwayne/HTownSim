#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// A stylized sky: the Simple Sky dome follows the camera and drifting low-poly clouds cross it. Both use an unlit,
    /// fog-free material so the scene fog does not wash them out. Cloud cover and tint follow <see cref="EnvironmentWeather"/>.
    /// </summary>
    internal sealed class SkyDecor : MonoBehaviour
    {
        /// <summary>How many clouds are in the sky. Raise it for a cloudier sky (costs a little frame rate); lower it for a clearer one.</summary>
        private const int CloudCount = 40;
        /// <summary>How far (metres) the sky dome is dropped below the horizon. Raise it to lower the dome edge and hide the horizon seam; lower it to lift it.</summary>
        private const float DomeDrop = 12f;
        /// <summary>The nearest (metres, horizontally) a cloud is placed to the centre. Raise it to keep clouds further out from overhead; lower it to allow clouds right overhead.</summary>
        private const float CloudRadiusMin = 60f;
        /// <summary>The furthest (metres, horizontally) a cloud is placed from the centre. Raise it to spread clouds wider over the horizon; lower it to bunch them.</summary>
        private const float CloudRadiusMax = 520f;
        /// <summary>The lowest (metres) a cloud floats. Raise it for a higher cloud base; lower it for lower, closer clouds.</summary>
        private const float CloudHeightMin = 150f;
        /// <summary>The highest (metres) a cloud floats. Raise it for taller layering; lower it to flatten the cloud layer.</summary>
        private const float CloudHeightMax = 250f;
        /// <summary>How fast (metres per second) the clouds drift. Raise it for fast-moving clouds; lower it (or 0) for a still sky.</summary>
        private const float WindSpeed = 2.5f;
        /// <summary>Clouds drifting past this distance (metres) wrap round to the other side. Raise it for a larger sky before clouds wrap; lower it to recycle them sooner.</summary>
        private const float WrapRadius = 600f;
        /// <summary>The share (0 to 1) of clouds shown in clear weather. Raise it for more clouds on a clear day; lower it for a cleaner blue sky.</summary>
        private const float ClearCloudFraction = 0.45f;

        /// <summary>The physics and render layer number the sky dome and clouds live on, hidden from every camera but the main one so they stay off the map. Change it only if layer 30 is used for something else.</summary>
        private const int SkyLayer = 30;

        /// <summary>How many frames pass between checks for the sky camera. Raise it to check less often (cheaper); lower it to react sooner when the camera changes.</summary>
        private const int FramesBetweenCameraChecks = 45;

        [SerializeField] private GameObject _dome = null!;

        [SerializeField] private GameObject[] _cloudPrefabs = new GameObject[0];

        [SerializeField] private Material _skyMaterial = null!;

        [SerializeField] private Material _cloudMaterial = null!;

        private readonly Transform[] _clouds = new Transform[CloudCount];

        private readonly float[] _baseScale = new float[CloudCount];

        private Transform _domeInstance = null!;

        private Material _domeMaterial = null!;

        private Material _cloudMaterialInstance = null!;

        private EnvironmentWeather? _weather;

        private Transform? _camera;

        /// <summary>Builds the dome and scatters the clouds.</summary>
        private void Start()
        {
            _weather = FindAnyObjectByType<EnvironmentWeather>();

            _domeMaterial = new Material(_skyMaterial);

            _cloudMaterialInstance = new Material(_cloudMaterial);

            var root = new GameObject("SkyDecor").transform;

            _domeInstance = Instantiate(_dome, root).transform;

            Dress(_domeInstance.gameObject, _domeMaterial);

            for (var i = 0; i < CloudCount; i++)
            {
                var prefab = _cloudPrefabs[i % _cloudPrefabs.Length];

                var cloud = Instantiate(prefab, root).transform;

                Dress(cloud.gameObject, _cloudMaterialInstance);

                var angle = Random.value * Mathf.PI * 2f;

                var radius = Random.Range(CloudRadiusMin, CloudRadiusMax);

                cloud.localPosition = new Vector3(Mathf.Cos(angle) * radius, Random.Range(CloudHeightMin, CloudHeightMax), Mathf.Sin(angle) * radius);

                _baseScale[i] = Random.Range(7f, 13f);

                cloud.localScale = Vector3.one * _baseScale[i];

                cloud.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);

                _clouds[i] = cloud;
            }

            _camera = Camera.main != null ? Camera.main.transform : null;

            HideFromOtherCameras();
        }

        private static void Dress(GameObject target, Material material)
        {
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length];

                for (var m = 0; m < materials.Length; m++)
                {
                    materials[m] = material;
                }

                renderer.sharedMaterials = materials;

                renderer.gameObject.layer = SkyLayer;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                renderer.receiveShadows = false;

                // A property block keeps the sky out of the GPU Resident Drawer, which needs a DOTS shader variant.
                var block = new MaterialPropertyBlock();

                block.SetFloat("_Unused", 0f);

                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Keeps the dome on the camera, drifts and wraps the clouds, and applies weather tint and cover.</summary>
        private void LateUpdate()
        {
            if (_camera == null)
            {
                _camera = Camera.main != null ? Camera.main.transform : null;

                if (_camera == null)
                {
                    return;
                }
            }

            if (Time.frameCount % FramesBetweenCameraChecks == 0)
            {
                HideFromOtherCameras();
            }

            var origin = _camera.position;

            _domeInstance.position = new Vector3(origin.x, origin.y - DomeDrop, origin.z);

            var cloudy = _weather != null ? _weather.CloudAmount : 0f;

            var rain = _weather != null ? _weather.RainAmount : 0f;

            var gray = Color.Lerp(Color.white, new Color(0.42f, 0.47f, 0.54f), cloudy) * (1f - 0.18f * rain);

            _domeMaterial.SetColor("_Tint", gray);

            _cloudMaterialInstance.SetColor("_Tint", Color.Lerp(Color.white, new Color(0.55f, 0.58f, 0.63f), cloudy));

            var visible = Mathf.RoundToInt(CloudCount * Mathf.Lerp(ClearCloudFraction, 1f, cloudy));

            for (var i = 0; i < CloudCount; i++)
            {
                var cloud = _clouds[i];

                cloud.gameObject.SetActive(i < visible);

                cloud.localScale = Vector3.one * (_baseScale[i] * (1f + cloudy * 0.7f));

                cloud.position += Vector3.right * (WindSpeed * Time.deltaTime);

                var offset = cloud.position - new Vector3(origin.x, cloud.position.y, origin.z);

                if (new Vector2(offset.x, offset.z).magnitude > WrapRadius)
                {
                    cloud.position = new Vector3(origin.x - WrapRadius * 0.95f, cloud.position.y, origin.z + Random.Range(-WrapRadius * 0.6f, WrapRadius * 0.6f));
                }
            }
        }

        /// <summary>Removes the sky layer from the culling mask of every camera except the main one (the minimap camera is created at runtime).</summary>
        private void HideFromOtherCameras()
        {
            foreach (var other in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (other.transform != _camera)
                {
                    other.cullingMask &= ~(1 << SkyLayer);
                }
            }
        }

        private void OnDestroy()
        {
            Destroy(_domeMaterial);

            Destroy(_cloudMaterialInstance);
        }
    }
}
