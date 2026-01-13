using System;
using System.Collections.Generic;
using Talk.Dialogue;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Talk.Runtime
{
    /// <summary>
    /// 씬에서 한 번만 배치하여 대화 데이터를 관리하고 DialoguePlayer에 넘겨줍니다.
    /// </summary>
    public class DialogueManager : MonoBehaviour
    {
        [Serializable]
        private class DialogueAsset
        {
            public string episodeId;
            public TextAsset jsonAsset;
        }

        [Serializable]
        private class EpisodeButton
        {
            public string episodeId;
            public Button button;
        }

        [Header("References")]
        [SerializeField] private DialoguePlayer player;
        [SerializeField] private GameObject episodeSelectionPanel;
        [SerializeField] private GameObject Title;

        [Header("Dialogues")]
        [SerializeField] private List<DialogueAsset> dialogueAssets = new List<DialogueAsset>();
        [SerializeField] private List<EpisodeButton> episodeButtons = new List<EpisodeButton>();

        private readonly Dictionary<string, TextAsset> _dialoguesByEpisodeId = new Dictionary<string, TextAsset>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TextAsset> _dialoguesByIndex = new List<TextAsset>();
        private readonly Dictionary<Button, UnityAction> _buttonListeners = new Dictionary<Button, UnityAction>();

        private void Awake()
        {
            if (player == null)
            {
                Debug.LogError($"{nameof(DialogueManager)}: DialoguePlayer 참조가 지정되지 않았습니다.");
            }
            else
            {
                player.EpisodeFinished += OnEpisodeFinished;
            }

            BuildLookup();
            SetupEpisodeButtons();
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.EpisodeFinished -= OnEpisodeFinished;
            }

            foreach (var pair in _buttonListeners)
            {
                if (pair.Key != null)
                {
                    pair.Key.onClick.RemoveListener(pair.Value);
                }
            }

            _buttonListeners.Clear();
        }

        /// <summary>
        /// episodeId로 지정한 대화를 재생합니다.
        /// </summary>
        public bool PlayByEpisodeId(string episodeId, string startingNodeId = null)
        {
            if (string.IsNullOrWhiteSpace(episodeId))
            {
                Debug.LogWarning($"{nameof(DialogueManager)}: episodeId가 비었습니다.");
                return false;
            }

            if (!_dialoguesByEpisodeId.TryGetValue(episodeId, out var textAsset))
            {
                Debug.LogWarning($"{nameof(DialogueManager)}: episodeId '{episodeId}'에 해당하는 대화를 찾지 못했습니다.");
                return false;
            }

            return PlayInternal(textAsset, startingNodeId);
        }

        /// <summary>
        /// 인덱스로 지정한 대화를 재생합니다. (씬에서 순서대로 지정한 경우에 사용)
        /// </summary>
        public bool PlayByIndex(int index, string startingNodeId = null)
        {
            if (index < 0 || index >= _dialoguesByIndex.Count)
            {
                Debug.LogWarning($"{nameof(DialogueManager)}: 인덱스 {index} 범위 밖입니다.");
                return false;
            }

            return PlayInternal(_dialoguesByIndex[index], startingNodeId);
        }

        /// <summary>
        /// 외부에서 직접 TextAsset을 지정하여 재생합니다.
        /// </summary>
        public bool PlayDialogue(TextAsset dialogueAsset, string startingNodeId = null)
        {
            if (dialogueAsset == null)
            {
                Debug.LogWarning($"{nameof(DialogueManager)}: 대화 TextAsset이 비었습니다.");
                return false;
            }

            return PlayInternal(dialogueAsset, startingNodeId);
        }

        private bool PlayInternal(TextAsset textAsset, string startingNodeId)
        {
            if (player == null)
            {
                Debug.LogWarning($"{nameof(DialogueManager)}: DialoguePlayer 참조가 없어 대화를 재생할 수 없습니다.");
                return false;
            }

            player.SetDialogueJson(textAsset, startingNodeId);
            return true;
        }

        private void BuildLookup()
        {
            _dialoguesByEpisodeId.Clear();
            _dialoguesByIndex.Clear();

            foreach (var entry in dialogueAssets)
            {
                if (entry == null || entry.jsonAsset == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.episodeId))
                {
                    if (_dialoguesByEpisodeId.ContainsKey(entry.episodeId))
                    {
                        Debug.LogWarning($"{nameof(DialogueManager)}: episodeId '{entry.episodeId}'가 중복되었습니다. 마지막 항목으로 덮어씁니다.");
                    }

                    _dialoguesByEpisodeId[entry.episodeId] = entry.jsonAsset;
                }

                _dialoguesByIndex.Add(entry.jsonAsset);
            }
        }

        private void SetupEpisodeButtons()
        {
            _buttonListeners.Clear();

            foreach (var buttonEntry in episodeButtons)
            {
                if (buttonEntry == null || buttonEntry.button == null || string.IsNullOrWhiteSpace(buttonEntry.episodeId))
                {
                    continue;
                }

                var episodeId = buttonEntry.episodeId;
                UnityAction callback = () => OnEpisodeButtonClicked(episodeId);
                buttonEntry.button.onClick.AddListener(callback);
                _buttonListeners[buttonEntry.button] = callback;
            }
        }

        private void OnEpisodeButtonClicked(string episodeId)
        {
            bool played = PlayByEpisodeId(episodeId);
            if (played)
            {
                SetEpisodeSelectionPanelActive(false);
            }
        }

        private void OnEpisodeFinished()
        {
            SetEpisodeSelectionPanelActive(true);
        }

        private void SetEpisodeSelectionPanelActive(bool active)
        {
            if (episodeSelectionPanel != null)
            {
                episodeSelectionPanel.SetActive(active);
                Title.SetActive(active);
            }
        }
    }
}

