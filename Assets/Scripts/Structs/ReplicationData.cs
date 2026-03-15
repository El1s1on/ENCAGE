using FishNet.Object.Prediction;

public struct ReplicationData : IReplicateData
{
    public PlayerInputData input;

    public ReplicationData(PlayerInputData input) : this()
    {
        this.input = input; 
    }

    private uint _tick;
    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
}

