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

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        continue;
                    }

                    var fields = ParseCsvLine(lines[i]);
                    if (fields.Count < 11)
                    {
                        Debug.LogWarning($"열이 11개 미만인 행이 발견되었습니다. (행 번호 {i + 1})");
                        continue;
                    }

                    string episodeId = fields[0];
                    if (!episodeMap.TryGetValue(episodeId, out var episodeData))
                    {
                        episodeData = new DialogueEpisodeData { episodeId = episodeId };
                        episodeMap.Add(episodeId, episodeData);
                    }

                    if (!int.TryParse(fields[3], out int order))
                    {
                        Debug.LogWarning($"order 값을 정수로 변환할 수 없습니다. (행 번호 {i + 1}, 값: {fields[3]})");
                        continue;
                    }

                    int position = 0;
                    if (!string.IsNullOrWhiteSpace(fields[6]))
                    {
                        if (!int.TryParse(fields[6], out position))
                        {
                            Debug.LogWarning($"position 값을 정수로 변환할 수 없습니다. (행 번호 {i + 1}, 값: {fields[6]})");
                            position = 0;
                        }
                    }

                    var lineData = new DialogueLineData
                    {
                        nodeId = fields[1],
                        lineId = fields[2],
                        order = order,
                        speaker = fields[4],
                        portraitKey = fields[5],
                        position = position,
                        productionKey = fields[7],
                        text = fields[8],
                        tags = fields[9],
                        nextNode = fields[10]
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
    }
}

