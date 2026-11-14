namespace hfcRed.ShaderToAnimatorBridge
{
    [System.Serializable]
    public class BridgeSettings
    {
        public string name = BridgeInstancer.SourceName;
        public int bits = 1;
        public bool sync = true;
        public string syncParameter = "";
        public bool localOnly = true;
        public bool stableOutput = true;

        public string ParameterPrefix => name.Replace(" ", "") + "/";
        public string LayerPrefix => name + " ";
        public string EmitterPath => name + "/Emitter";
        public string SyncParameter => !sync ? "" : string.IsNullOrEmpty(syncParameter) ? ParameterPrefix + "Synced" : syncParameter;
    }
}
