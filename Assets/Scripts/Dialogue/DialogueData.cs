using System;
using System.Collections.Generic;

namespace Talk.Dialogue
{
    [Serializable]
    public class DialogueEpisodeData
    {
        public string episodeId;
        public List<DialogueLineData> lines = new List<DialogueLineData>();
    }

    [Serializable]
    public class DialogueLineData
    {
        public string nodeId;
        public string lineId;
        public int order;
        public string speaker;
        public string portraitKey;
        public int position;
        public string productionKey;
        public string BGICode;
        public string BGMCode;
        public string spriteType;
        public string text;
        public string tags;
        public string nextNode;
    }
}

