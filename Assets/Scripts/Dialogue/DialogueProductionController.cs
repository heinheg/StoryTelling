using System;
using System.Collections;
using System.Collections.Generic;
using Talk.Dialogue;
using UnityEngine;
using UnityEngine.UI;

namespace Talk.Runtime
{
    /// <summary>
    /// productionKey에 대응하는 연출을 처리합니다.
    /// </summary>
    public class DialogueProductionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DialoguePlayer player;
        [SerializeField] private RectTransform screenShakeTarget;

        [Header("Jump Effect")]
        [SerializeField] private float jumpHeight = 40f;
        [SerializeField] private float jumpDuration = 0.25f;

        [Header("Screen Shake Effect")]
        [SerializeField] private float shakeDuration = 0.4f;
        [SerializeField] private float shakeStrength = 20f;
        [SerializeField] private bool shakeAffectX = true;
        [SerializeField] private bool shakeAffectY = true;

        private readonly Dictionary<RectTransform, Coroutine> _jumpRoutines = new Dictionary<RectTransform, Coroutine>();
        private readonly Dictionary<RectTransform, Vector2> _jumpOriginalPositions = new Dictionary<RectTransform, Vector2>();
        private Coroutine _shakeRoutine;
        private Vector2 _shakeOriginalPosition;

        private void Awake()
        {
            if (player == null)
            {
                Debug.LogError($"{nameof(DialogueProductionController)}: DialoguePlayer 참조가 지정되지 않았습니다.");
                enabled = false;
                return;
            }

            player.LinePresented += OnLinePresented;
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.LinePresented -= OnLinePresented;
            }

            foreach (var pair in _jumpRoutines)
            {
                if (pair.Value != null)
                {
                    StopCoroutine(pair.Value);
                }

                if (pair.Key != null && _jumpOriginalPositions.TryGetValue(pair.Key, out var original))
                {
                    pair.Key.anchoredPosition = original;
                }
            }

            _jumpRoutines.Clear();
            _jumpOriginalPositions.Clear();

            if (screenShakeTarget != null && _shakeRoutine != null)
            {
                StopCoroutine(_shakeRoutine);
                ResetScreenShake();
            }
        }

        private void OnLinePresented(DialogueLineData line)
        {
            if (line == null || string.IsNullOrWhiteSpace(line.productionKey))
            {
                return;
            }

            var tokens = line.productionKey.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var key = token.Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                HandleProductionKey(key.ToLowerInvariant(), line);
            }
        }

        private void HandleProductionKey(string key, DialogueLineData line)
        {
            switch (key)
            {
                case "jump2":
                    TriggerJump(line, 2);
                    break;
                case "shake1":
                    TriggerScreenShake();
                    break;
                default:
                    break;
            }
        }

        private void TriggerJump(DialogueLineData line, int jumpCount)
        {
            if (player == null)
            {
                return;
            }

            var image = player.GetPrimaryPortrait(line.portraitKey);
            if (image == null)
            {
                return;
            }

            var rect = image.rectTransform;
            if (rect == null)
            {
                return;
            }

            if (_jumpRoutines.TryGetValue(rect, out var existingRoutine) && existingRoutine != null)
            {
                StopCoroutine(existingRoutine);
                if (_jumpOriginalPositions.TryGetValue(rect, out var previousOriginal))
                {
                    rect.anchoredPosition = previousOriginal;
                }
                _jumpRoutines.Remove(rect);
                _jumpOriginalPositions.Remove(rect);
            }

            var original = rect.anchoredPosition;
            _jumpOriginalPositions[rect] = original;

            var routine = StartCoroutine(JumpRoutine(rect, original, jumpCount));
            _jumpRoutines[rect] = routine;
        }

        private IEnumerator JumpRoutine(RectTransform target, Vector2 originalPosition, int jumpCount)
        {
            var upPosition = originalPosition + Vector2.up * jumpHeight;
            float halfDuration = Mathf.Max(0.01f, jumpDuration * 0.5f);

            for (int i = 0; i < jumpCount; i++)
            {
                yield return AnimateAnchoredPosition(target, originalPosition, upPosition, halfDuration);
                yield return AnimateAnchoredPosition(target, upPosition, originalPosition, halfDuration);
            }

            target.anchoredPosition = originalPosition;
            _jumpRoutines.Remove(target);
            _jumpOriginalPositions.Remove(target);
        }

        private IEnumerator AnimateAnchoredPosition(RectTransform target, Vector2 from, Vector2 to, float duration)
        {
            if (duration <= 0f)
            {
                target.anchoredPosition = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                target.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
                yield return null;
            }

            target.anchoredPosition = to;
        }

        private void TriggerScreenShake()
        {
            if (screenShakeTarget == null)
            {
                return;
            }

            if (_shakeRoutine != null)
            {
                StopCoroutine(_shakeRoutine);
                ResetScreenShake();
            }

            _shakeOriginalPosition = screenShakeTarget.anchoredPosition;
            _shakeRoutine = StartCoroutine(ScreenShakeRoutine());
        }

        private IEnumerator ScreenShakeRoutine()
        {
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, shakeDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float remaining = 1f - Mathf.Clamp01(elapsed / duration);

                float offsetX = shakeAffectX ? UnityEngine.Random.Range(-1f, 1f) * shakeStrength * remaining : 0f;
                float offsetY = shakeAffectY ? UnityEngine.Random.Range(-1f, 1f) * shakeStrength * remaining : 0f;

                screenShakeTarget.anchoredPosition = _shakeOriginalPosition + new Vector2(offsetX, offsetY);
                yield return null;
            }

            ResetScreenShake();
        }

        private void ResetScreenShake()
        {
            if (screenShakeTarget != null)
            {
                screenShakeTarget.anchoredPosition = _shakeOriginalPosition;
            }

            _shakeRoutine = null;
        }
    }
}

