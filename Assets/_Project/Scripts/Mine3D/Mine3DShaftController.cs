using System.Collections;
using UnityEngine;

namespace Project.Mine3D
{
    /// <summary>
    /// Три грани шахты (лёгкая / средняя / тяжёлая). Смена сложности крутит корень вокруг Y.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Mine3DShaftController : MonoBehaviour
    {
        public const string Easy = "easy";
        public const string Medium = "medium";
        public const string Hard = "hard";

        [SerializeField] private Transform shaftRoot;
        [SerializeField] private float rotateDuration = 0.55f;
        [SerializeField] private string currentDifficulty = Easy;

        private Coroutine _rotateRoutine;
        private float _currentYaw;

        public string CurrentDifficulty => currentDifficulty;
        public Transform ShaftRoot => shaftRoot;

        public void Bind(Transform root)
        {
            shaftRoot = root;
            _currentYaw = root != null ? root.localEulerAngles.y : 0f;
            if (shaftRoot != null)
                ApplyFaceLighting(currentDifficulty);
        }

        public void SetDifficultyImmediate(string difficulty)
        {
            currentDifficulty = Normalize(difficulty);
            _currentYaw = YawFor(currentDifficulty);
            if (shaftRoot != null)
                shaftRoot.localRotation = Quaternion.Euler(0f, _currentYaw, 0f);
            ApplyFaceLighting(currentDifficulty);
            Mine3DUiBevel.ApplyDifficultySelection(currentDifficulty);
        }

        public void SetDifficultyAnimated(string difficulty)
        {
            difficulty = Normalize(difficulty);
            if (string.Equals(currentDifficulty, difficulty, System.StringComparison.OrdinalIgnoreCase))
                return;

            var from = YawFor(currentDifficulty);
            var to = YawFor(difficulty);
            // Кратчайший путь по часовой / против — выбираем ближайший угол.
            to = from + Mathf.DeltaAngle(from, to);
            currentDifficulty = difficulty;
            ApplyFaceLighting(currentDifficulty);
            Mine3DUiBevel.ApplyDifficultySelection(currentDifficulty);

            if (_rotateRoutine != null)
                StopCoroutine(_rotateRoutine);
            _rotateRoutine = StartCoroutine(RotateRoutine(from, to));
        }

        private void ApplyFaceLighting(string difficulty)
        {
            ApplyFaceVisibility(difficulty, showAllGeometry: false);
        }

        private void ApplyFaceVisibility(string difficulty, bool showAllGeometry)
        {
            if (shaftRoot == null) return;
            for (var i = 0; i < shaftRoot.childCount; i++)
            {
                var face = shaftRoot.GetChild(i);
                if (face == null || !face.name.StartsWith("Face_", System.StringComparison.Ordinal))
                    continue;

                var active = string.Equals(face.name, "Face_" + difficulty, System.StringComparison.OrdinalIgnoreCase);
                var showGeometry = showAllGeometry || active;

                var renderers = face.GetComponentsInChildren<Renderer>(true);
                for (var ri = 0; ri < renderers.Length; ri++)
                {
                    var r = renderers[ri];
                    if (r == null) continue;
                    // Текст барьера — только на активной грани (иначе просвечивает с боков).
                    var isText = r.GetComponent<TextMesh>() != null;
                    r.enabled = isText ? active : showGeometry;
                }

                var lights = face.GetComponentsInChildren<Light>(true);
                for (var li = 0; li < lights.Length; li++)
                {
                    if (lights[li] != null)
                        lights[li].enabled = active;
                }

                // Клики только по активной грани (во время поворота — по всем видимым).
                var colliders = face.GetComponentsInChildren<Collider>(true);
                var collidersOn = showAllGeometry || active;
                for (var ci = 0; ci < colliders.Length; ci++)
                {
                    if (colliders[ci] != null)
                        colliders[ci].enabled = collidersOn;
                }
            }
        }

        private IEnumerator RotateRoutine(float fromYaw, float toYaw)
        {
            // Во время поворота показываем все грани, текст — только у целевой.
            ApplyFaceVisibility(currentDifficulty, showAllGeometry: true);

            var t = 0f;
            var duration = Mathf.Max(0.05f, rotateDuration);
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                _currentYaw = Mathf.Lerp(fromYaw, toYaw, k);
                if (shaftRoot != null)
                    shaftRoot.localRotation = Quaternion.Euler(0f, _currentYaw, 0f);
                yield return null;
            }

            _currentYaw = toYaw;
            if (shaftRoot != null)
                shaftRoot.localRotation = Quaternion.Euler(0f, _currentYaw, 0f);
            ApplyFaceVisibility(currentDifficulty, showAllGeometry: false);
            _rotateRoutine = null;
        }

        public static float YawFor(string difficulty)
        {
            return Normalize(difficulty) switch
            {
                Medium => -120f,
                Hard => -240f,
                _ => 0f
            };
        }

        public static float FaceLocalYaw(string difficulty)
        {
            return Normalize(difficulty) switch
            {
                Medium => 120f,
                Hard => 240f,
                _ => 0f
            };
        }

        private static string Normalize(string difficulty)
        {
            if (string.IsNullOrWhiteSpace(difficulty)) return Easy;
            difficulty = difficulty.Trim().ToLowerInvariant();
            if (difficulty is "medium" or "normal" or "средняя") return Medium;
            if (difficulty is "hard" or "тяжёлая" or "тяжелая") return Hard;
            return Easy;
        }
    }
}
