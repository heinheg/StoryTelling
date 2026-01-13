using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Talk.Dialogue.Editor
{
    public static class DialogueCsvToJsonExporter
    {
        private const string CsvFileName = "dialogue_lines.csv";

        [MenuItem("Tools/Scenario/Export Dialogue CSV To JSON")]
        public static void Export()
        {
            string csvPath = Path.Combine(Application.dataPath, "Scenario/CSV", CsvFileName);
            if (!File.Exists(csvPath))
            {
                Debug.LogError($"CSV 파일을 찾을 수 없습니다: {csvPath}");
                return;
            }

            string jsonDirectory = Path.Combine(Application.dataPath, "Scenario/Json");
            if (!Directory.Exists(jsonDirectory))
            {
                Directory.CreateDirectory(jsonDirectory);
            }

            try
            {
                var episodeMap = new Dictionary<string, DialogueEpisodeData>(StringComparer.OrdinalIgnoreCase);
                var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
                if (lines.Length <= 1)
                {
                    Debug.LogWarning("CSV 파일에 변환할 데이터가 없습니다.");
                    return;
                }

                var headerFields = ParseCsvLine(lines[0]);
                var headerIndex = BuildHeaderIndex(headerFields);

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        continue;
                    }

                    var fields = ParseCsvLine(lines[i]);

                    string episodeId = GetField(fields, headerIndex, "episodeId");
                    if (string.IsNullOrEmpty(episodeId))
                    {
                        Debug.LogWarning($"episodeId가 비어 있는 행이 발견되었습니다. (행 번호 {i + 1})");
                        continue;
                    }

                    if (!episodeMap.TryGetValue(episodeId, out var episodeData))
                    {
                        episodeData = new DialogueEpisodeData { episodeId = episodeId };
                        episodeMap.Add(episodeId, episodeData);
                    }

                    string orderField = GetField(fields, headerIndex, "order");
                    if (!int.TryParse(orderField, out int order))
                    {
                        Debug.LogWarning($"order 값을 정수로 변환할 수 없습니다. (행 번호 {i + 1}, 값: {orderField})");
                        continue;
                    }

                    int position = 0;
                    string positionField = GetField(fields, headerIndex, "position");
                    if (!string.IsNullOrWhiteSpace(positionField))
                    {
                        if (!int.TryParse(positionField, out position))
                        {
                            Debug.LogWarning($"position 값을 정수로 변환할 수 없습니다. (행 번호 {i + 1}, 값: {positionField})");
                            position = 0;
                        }
                    }

                    var lineData = new DialogueLineData
                    {
                        nodeId = GetField(fields, headerIndex, "nodeId"),
                        lineId = GetField(fields, headerIndex, "lineId"),
                        order = order,
                        speaker = GetField(fields, headerIndex, "speaker"),
                        portraitKey = GetField(fields, headerIndex, "portraitKey"),
                        position = position,
                        productionKey = GetField(fields, headerIndex, "productionKey"),
                        BGICode = GetField(fields, headerIndex, "BGICode"),
                        BGMCode = GetField(fields, headerIndex, "BGMCode"),
                        spriteType = GetField(fields, headerIndex, "SpriteType"),
                        text = GetField(fields, headerIndex, "text"),
                        tags = GetField(fields, headerIndex, "tags"),
                        nextNode = GetField(fields, headerIndex, "nextNode")
                    };

                    episodeData.lines.Add(lineData);
                }

                foreach (var episode in episodeMap.Values)
                {
                    episode.lines.Sort((a, b) => a.order.CompareTo(b.order));

                    string json = JsonUtility.ToJson(episode, true);
                    string jsonPath = Path.Combine(jsonDirectory, $"{episode.episodeId}.json");
                    File.WriteAllText(jsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }

                AssetDatabase.Refresh();
                Debug.Log($"대화 CSV 변환이 완료되었습니다. 변환된 에피소드 수: {episodeMap.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"CSV 변환 중 오류가 발생했습니다: {ex}");
            }
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var builder = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char current = line[i];
                switch (current)
                {
                    case '"':
                        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                        {
                            builder.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = !inQuotes;
                        }

                        break;
                    case ',':
                        if (inQuotes)
                        {
                            builder.Append(current);
                        }
                        else
                        {
                            result.Add(builder.ToString());
                            builder.Clear();
                        }

                        break;
                    default:
                        builder.Append(current);
                        break;
                }
            }

            result.Add(builder.ToString());

            for (int i = 0; i < result.Count; i++)
            {
                result[i] = result[i].Trim().Replace("\r", string.Empty);
            }

            return result;
        }

        private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> headers)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                if (!string.IsNullOrWhiteSpace(header) && !map.ContainsKey(header))
                {
                    map.Add(header, i);
                }
            }

            return map;
        }

        private static string GetField(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> headerIndex, string header)
        {
            if (!headerIndex.TryGetValue(header, out var index) || index < 0 || index >= fields.Count)
            {
                return string.Empty;
            }

            return fields[index];
        }
    }
}

