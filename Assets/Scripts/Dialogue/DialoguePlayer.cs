using System;
using System.Collections.Generic;
using Talk.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Talk.Runtime
{
    /// <summary>
    /// JSON 기반 대화 데이터를 순차적으로 재생하면서 UI와 캐릭터 스프라이트를 갱신합니다.
    /// </summary>
    public class DialoguePlayer : MonoBehaviour
    {
        [Serializable]
        private class PortraitPrefab
        {
            public string portraitKey;
            public GameObject prefab;
        }

        [Serializable]
        private class SpriteMapping
        {
            public string spriteType;
            public Sprite sprite;
        }

        private class PortraitInstance
        {
            public string portraitKey;
            public RectTransform root;
            public Image image;
            public int currentPositionKey = int.MinValue;
        }

        [Serializable]
        private class PositionAnchor
        {
            public int positionKey;
            public RectTransform anchor;
        }

        [Header("Data")]
        [SerializeField] private TextAsset dialogueJson;
        [SerializeField] private string startingNodeId;

        [Header("UI References")]
        [SerializeField] private TMP_Text speakerLabel;
        [SerializeField] private TMP_Text lineLabel;

        [Header("Presentation")]
        [SerializeField] private float characterInterval = 0.02f;
        [SerializeField, Range(0f, 1f)] private float inactivePortraitAlpha = 0.35f;
        [SerializeField] private List<PortraitPrefab> portraitPrefabs = new List<PortraitPrefab>();
        [SerializeField] private List<SpriteMapping> spriteMappings = new List<SpriteMapping>();
        [SerializeField] private Image backgroundImage;
        [SerializeField] private List<SpriteMapping> backgroundMappings = new List<SpriteMapping>();
        [SerializeField] private bool advanceOnPointerDown = true;
        [SerializeField] private List<PositionAnchor> positionAnchors = new List<PositionAnchor>();
        [SerializeField] private RectTransform portraitRoot;

        private DialogueEpisodeData _episodeData;
        private readonly Dictionary<string, DialogueLineData> _linesByNodeId = new Dictionary<string, DialogueLineData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _indexByNodeId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeKeyBuffer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PortraitPrefab> _portraitLookup = new Dictionary<string, PortraitPrefab>(StringComparer.OrdinalIgnoreCase);
        private List<DialogueLineData> _orderedLines = new List<DialogueLineData>();
        private readonly Dictionary<int, RectTransform> _positionAnchorLookup = new Dictionary<int, RectTransform>();
        private readonly Dictionary<string, PortraitInstance> _activePortraitInstances = new Dictionary<string, PortraitInstance>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, PortraitInstance> _positionOccupants = new Dictionary<int, PortraitInstance>();
        private readonly Dictionary<string, Sprite> _spriteLookup = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Sprite> _backgroundLookup = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private Sprite _defaultBackgroundSprite;

        private DialogueLineData _currentLine;
        private Coroutine _typingRoutine;
        private bool _isTyping;
        private bool _skipTyping;

        public event Action EpisodeFinished;
        public event Action<DialogueLineData> LinePresented;

#if UNITY_EDITOR
        private void OnValidate()
        {
            CachePortraits();
            CachePositionAnchors();
            CacheSpriteMappings();
            CacheBackgroundMappings();
            CaptureDefaultBackground();
        }
#endif

        private void Awake()
        {
            CachePortraits();
            CachePositionAnchors();
            CacheSpriteMappings();
            CacheBackgroundMappings();
            CaptureDefaultBackground();
            LoadEpisode();
        }

        private void Start()
        {
            if (_episodeData != null)
            {
                BeginEpisode();
            }
        }

        /// <summary>
        /// 외부에서 대화 JSON을 교체할 수 있도록 제공합니다.
        /// </summary>
        public void SetDialogueJson(TextAsset jsonAsset, string nodeId = null)
        {
            dialogueJson = jsonAsset;
            startingNodeId = nodeId ?? string.Empty;
            LoadEpisode();
            if (isActiveAndEnabled && _episodeData != null)
            {
                BeginEpisode();
            }
        }

        public void OnDialogueClicked()
        {
            if (_episodeData == null)
            {
                return;
            }

            if (_isTyping)
            {
                _skipTyping = true;
                return;
            }

            var nextLine = ResolveNextLine(_currentLine);
            if (nextLine != null)
            {
                PresentLine(nextLine);
            }
            else
            {
                CleanupPortraitInstances();
                EpisodeFinished?.Invoke();
            }
        }

        public Image GetPrimaryPortrait(string portraitKeyField)
        {
            string key = ExtractPrimaryPortraitKey(portraitKeyField);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            var instance = EnsurePortraitInstance(key);
            return instance?.image;
        }

        private void Update()
        {
            if (!advanceOnPointerDown)
            {
                return;
            }

            if (!isActiveAndEnabled || _episodeData == null)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                OnDialogueClicked();
                return;
            }

            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    OnDialogueClicked();
                }
            }
        }

        private void CachePortraits()
        {
            _portraitLookup.Clear();
            foreach (var entry in portraitPrefabs)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.portraitKey) || entry.prefab == null)
                {
                    continue;
                }

                _portraitLookup[entry.portraitKey] = entry;
            }
        }

        private void CacheSpriteMappings()
        {
            _spriteLookup.Clear();
            foreach (var mapping in spriteMappings)
            {
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.spriteType) || mapping.sprite == null)
                {
                    continue;
                }

                var key = mapping.spriteType.Trim();
                if (key.Length == 0 || _spriteLookup.ContainsKey(key))
                {
                    continue;
                }

                _spriteLookup.Add(key, mapping.sprite);
            }
        }

        private void CacheBackgroundMappings()
        {
            _backgroundLookup.Clear();
            foreach (var mapping in backgroundMappings)
            {
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.spriteType) || mapping.sprite == null)
                {
                    continue;
                }

                var key = mapping.spriteType.Trim();
                if (key.Length == 0 || _backgroundLookup.ContainsKey(key))
                {
                    continue;
                }

                _backgroundLookup.Add(key, mapping.sprite);
            }
        }

        private void CaptureDefaultBackground()
        {
            if (backgroundImage == null)
            {
                _defaultBackgroundSprite = null;
                return;
            }

            if (!Application.isPlaying || _defaultBackgroundSprite == null)
            {
                _defaultBackgroundSprite = backgroundImage.sprite;
            }
        }

        private void CachePositionAnchors()
        {
            _positionAnchorLookup.Clear();
            foreach (var anchor in positionAnchors)
            {
                if (anchor == null || anchor.anchor == null)
                {
                    continue;
                }

                _positionAnchorLookup[anchor.positionKey] = anchor.anchor;
            }
        }

        private void LoadEpisode()
        {
            _episodeData = null;
            _linesByNodeId.Clear();
            _indexByNodeId.Clear();
            _orderedLines.Clear();

            if (dialogueJson == null)
            {
                Debug.LogWarning($"{nameof(DialoguePlayer)}: 대화 JSON(TextAsset)이 지정되지 않았습니다.");
                return;
            }

            try
            {
                _episodeData = JsonUtility.FromJson<DialogueEpisodeData>(dialogueJson.text);
                if (_episodeData == null || _episodeData.lines == null || _episodeData.lines.Count == 0)
                {
                    Debug.LogWarning($"{nameof(DialoguePlayer)}: JSON에 대화 데이터가 없습니다.");
                    _episodeData = null;
                    return;
                }

                _orderedLines = new List<DialogueLineData>(_episodeData.lines);
                _orderedLines.Sort((a, b) => a.order.CompareTo(b.order));

                for (int i = 0; i < _orderedLines.Count; i++)
                {
                    var line = _orderedLines[i];
                    if (string.IsNullOrEmpty(line.nodeId))
                    {
                        Debug.LogWarning($"nodeId가 비어 있는 행이 있습니다. (order: {line.order})");
                        continue;
                    }

                    _linesByNodeId[line.nodeId] = line;
                    _indexByNodeId[line.nodeId] = i;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(DialoguePlayer)}: JSON 파싱 중 오류가 발생했습니다. {ex}");
                _episodeData = null;
            }
        }

        private void BeginEpisode()
        {
            DialogueLineData startLine = null;
            if (!string.IsNullOrEmpty(startingNodeId) && _linesByNodeId.TryGetValue(startingNodeId, out var fromNode))
            {
                startLine = fromNode;
            }
            else if (_orderedLines.Count > 0)
            {
                startLine = _orderedLines[0];
            }

            if (startLine == null)
            {
                Debug.LogWarning($"{nameof(DialoguePlayer)}: 시작할 라인을 찾지 못했습니다.");
                return;
            }

            PresentLine(startLine);
        }

        private void PresentLine(DialogueLineData line)
        {
            _currentLine = line;
            UpdateSpeakerUi(line);
            UpdatePortraits(line.portraitKey);
            ApplyPosition(line);
            ApplyBackground(line);
            ApplySpriteAppearance(line);
            StartTyping(line.text);
            LinePresented?.Invoke(line);
        }

        private void UpdateSpeakerUi(DialogueLineData line)
        {
            if (speakerLabel != null)
            {
                speakerLabel.text = line.speaker ?? string.Empty;
            }

            if (lineLabel != null)
            {
                lineLabel.text = string.Empty;
            }
        }

        private void UpdatePortraits(string portraitKey)
        {
            _activeKeyBuffer.Clear();

            if (!string.IsNullOrWhiteSpace(portraitKey))
            {
                var keys = portraitKey.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var key in keys)
                {
                    var trimmed = key.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        _activeKeyBuffer.Add(trimmed);
                        EnsurePortraitInstance(trimmed);
                    }
                }

                if (_activeKeyBuffer.Count == 0 && EnsurePortraitInstance(portraitKey) != null)
                {
                    _activeKeyBuffer.Add(portraitKey);
                }
            }

            foreach (var kvp in _activePortraitInstances)
            {
                var instance = kvp.Value;
                if (instance?.image == null)
                {
                    continue;
                }

                bool isActive = _activeKeyBuffer.Contains(kvp.Key);
                SetImageAlpha(instance.image, isActive ? 1f : inactivePortraitAlpha);
            }
        }

        private void ApplyPosition(DialogueLineData line)
        {
            if (line == null)
            {
                return;
            }

            string portraitKey = ExtractPrimaryPortraitKey(line.portraitKey);
            if (string.IsNullOrEmpty(portraitKey))
            {
                return;
            }

            var instance = EnsurePortraitInstance(portraitKey);
            if (instance == null || instance.root == null)
            {
                return;
            }

            if (!_positionAnchorLookup.TryGetValue(line.position, out var anchor) || anchor == null)
            {
                return;
            }

            SetPortraitToAnchor(instance, anchor, line.position);
        }

        private void ApplyBackground(DialogueLineData line)
        {
            if (backgroundImage == null)
            {
                return;
            }

            string code = line?.BGICode;
            if (string.IsNullOrWhiteSpace(code))
            {
                // 아무 코드도 없으면 현재 배경을 유지합니다.
                return;
            }

            var key = code.Trim();
            if (_backgroundLookup.TryGetValue(key, out var sprite) && sprite != null)
            {
                backgroundImage.sprite = sprite;
            }
        }

        private void ApplySpriteAppearance(DialogueLineData line)
        {
            if (line == null)
            {
                return;
            }

            string portraitKey = ExtractPrimaryPortraitKey(line.portraitKey);
            if (string.IsNullOrEmpty(portraitKey))
            {
                return;
            }

            var instance = EnsurePortraitInstance(portraitKey);
            if (instance == null)
            {
                return;
            }

            ApplySprite(portraitKey, instance, line.spriteType);
        }

        private void SetImageAlpha(Image image, float alpha)
        {
            var color = image.color;
            color.a = Mathf.Clamp01(alpha);
            image.color = color;
        }

        private string ExtractPrimaryPortraitKey(string portraitKeyField)
        {
            if (string.IsNullOrWhiteSpace(portraitKeyField))
            {
                return string.Empty;
            }

            var keys = portraitKeyField.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var key in keys)
            {
                var trimmed = key.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    return trimmed;
                }
            }

            return portraitKeyField.Trim();
        }

        private PortraitInstance EnsurePortraitInstance(string portraitKey)
        {
            if (string.IsNullOrWhiteSpace(portraitKey))
            {
                return null;
            }

            if (_activePortraitInstances.TryGetValue(portraitKey, out var existing) && existing != null)
            {
                if (existing.root == null)
                {
                    if (existing.currentPositionKey != int.MinValue &&
                        _positionOccupants.TryGetValue(existing.currentPositionKey, out var occupant) &&
                        occupant == existing)
                    {
                        _positionOccupants.Remove(existing.currentPositionKey);
                    }

                    _activePortraitInstances.Remove(portraitKey);
                }
                else
                {
                    return existing;
                }
            }

            if (!_portraitLookup.TryGetValue(portraitKey, out var prefabEntry) || prefabEntry == null || prefabEntry.prefab == null)
            {
                Debug.LogWarning($"{nameof(DialoguePlayer)}: portraitKey '{portraitKey}'에 해당하는 프리팹을 찾을 수 없습니다.");
                return null;
            }

            var parent = portraitRoot != null ? portraitRoot : transform as RectTransform;
            var instanceGo = Instantiate(prefabEntry.prefab, parent);
            var rect = instanceGo.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = instanceGo.AddComponent<RectTransform>();
            }

            var image = instanceGo.GetComponentInChildren<Image>(true);
            if (image == null)
            {
                Debug.LogWarning($"{nameof(DialoguePlayer)}: portraitKey '{portraitKey}' 프리팹에 Image 컴포넌트가 없습니다.");
            }

            var instance = new PortraitInstance
            {
                portraitKey = portraitKey,
                root = rect,
                image = image
            };

            _activePortraitInstances[portraitKey] = instance;
            return instance;
        }

        private void ApplySprite(string portraitKey, PortraitInstance instance, string spriteType)
        {
            if (instance == null || instance.image == null)
            {
                return;
            }

            Sprite targetSprite = null;

            if (!string.IsNullOrWhiteSpace(spriteType))
            {
                var key = spriteType.Trim();
                if (_spriteLookup.TryGetValue(key, out var mappedSprite))
                {
                    targetSprite = mappedSprite;
                }
            }

            if (targetSprite == null)
            {
                if (_portraitLookup.TryGetValue(portraitKey, out var prefabEntry) && prefabEntry != null && prefabEntry.prefab != null)
                {
                    var prefabImage = prefabEntry.prefab.GetComponentInChildren<Image>(true);
                    if (prefabImage != null)
                    {
                        targetSprite = prefabImage.sprite;
                    }
                }
            }

            if (targetSprite != null && instance.image.sprite != targetSprite)
            {
                instance.image.sprite = targetSprite;
            }
        }

        private void SetPortraitToAnchor(PortraitInstance instance, RectTransform anchor, int positionKey)
        {
            if (instance == null || anchor == null)
            {
                return;
            }

            var root = instance.root;
            if (root == null)
            {
                return;
            }

            if (_positionOccupants.TryGetValue(positionKey, out var previousOccupant) && previousOccupant != null && previousOccupant != instance)
            {
                RemovePortraitInstance(previousOccupant);
            }

            if (instance.currentPositionKey != int.MinValue &&
                _positionOccupants.TryGetValue(instance.currentPositionKey, out var recorded) &&
                recorded == instance)
            {
                _positionOccupants.Remove(instance.currentPositionKey);
            }

            root.SetParent(anchor, false);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            instance.currentPositionKey = positionKey;
            _positionOccupants[positionKey] = instance;
        }

        private void RemovePortraitInstance(PortraitInstance instance)
        {
            if (instance == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(instance.portraitKey) &&
                _activePortraitInstances.TryGetValue(instance.portraitKey, out var stored) &&
                stored == instance)
            {
                _activePortraitInstances.Remove(instance.portraitKey);
            }

            if (instance.currentPositionKey != int.MinValue &&
                _positionOccupants.TryGetValue(instance.currentPositionKey, out var occupant) &&
                occupant == instance)
            {
                _positionOccupants.Remove(instance.currentPositionKey);
            }

            if (instance.root != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(instance.root.gameObject);
                }
                else
                {
                    DestroyImmediate(instance.root.gameObject);
                }
            }

            instance.root = null;
            instance.image = null;
            instance.currentPositionKey = int.MinValue;
        }

        private void CleanupPortraitInstances()
        {
            var instances = new List<PortraitInstance>(_activePortraitInstances.Values);
            foreach (var instance in instances)
            {
                RemovePortraitInstance(instance);
            }

            _activePortraitInstances.Clear();
            _positionOccupants.Clear();

            if (backgroundImage != null && _defaultBackgroundSprite != null)
            {
                backgroundImage.sprite = _defaultBackgroundSprite;
            }
        }

        private DialogueLineData ResolveNextLine(DialogueLineData current)
        {
            if (current == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(current.nextNode) &&
                _linesByNodeId.TryGetValue(current.nextNode, out var byNextNode))
            {
                return byNextNode;
            }

            if (_indexByNodeId.TryGetValue(current.nodeId, out var index))
            {
                int candidateIndex = index + 1;
                if (candidateIndex >= 0 && candidateIndex < _orderedLines.Count)
                {
                    return _orderedLines[candidateIndex];
                }
            }

            return null;
        }

        private void StartTyping(string text)
        {
            if (lineLabel == null)
            {
                return;
            }

            if (_typingRoutine != null)
            {
                StopCoroutine(_typingRoutine);
            }

            _typingRoutine = StartCoroutine(TypeLineRoutine(text ?? string.Empty));
        }

        private System.Collections.IEnumerator TypeLineRoutine(string text)
        {
            _isTyping = true;
            _skipTyping = false;
            lineLabel.text = string.Empty;

            foreach (char character in text)
            {
                if (_skipTyping)
                {
                    lineLabel.text = text;
                    break;
                }

                lineLabel.text += character;
                yield return new WaitForSeconds(characterInterval);
            }

            lineLabel.text = text;
            _isTyping = false;
            _typingRoutine = null;
        }
    }
}

