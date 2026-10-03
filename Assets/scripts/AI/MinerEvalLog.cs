using System.Globalization;
using System.IO;

// Test (evaluation) log for the miner AI: one CSV row per level and one per shot.
// MinerAgent turns it on when the game is started with -minerEvalLog <folder>
// (carc/evaluate.slurm does this). carc/report.py turns the files into graphs.
// Each game process writes its own pair of files, so several copies can run at once.
public class MinerEvalLog
{
    public struct Shot
    {
        public int episode;
        public int index;            // 1st, 2nd, ... shot of the level
        public float fireTime;       // seconds into the level
        public float fireFraction;   // 0 = level start, 1 = time up
        public float aimAngle;       // degrees: 180 left, 270 down, 360 right
        public float waited;         // seconds the hook was ready before the AI fired
        public string predicted;     // what the aim pointed at when firing: Nothing / Mineral / Player
        public float predictedValue;
        public float predictedDistance;
        public string outcome;       // Gold / Rock / dimond / Empty / Player / Unfinished
        public float value;
        public float weight;
        public float retrieveTime;   // seconds from firing until the hook was back (or caught the player)
        public float moneyAfter;
    }

    private readonly StreamWriter episodes;
    private readonly StreamWriter shots;
    private readonly string label;

    public MinerEvalLog(string folder, string label)
    {
        this.label = label;
        Directory.CreateDirectory(folder);
        string prefix = Path.Combine(folder, $"{label}_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}");
        episodes = Open(prefix + "_episodes.csv",
            "label,episode,level_target,time_limit,money,target_reached,player_caught,catch_time,catch_time_fraction,shots,empty_shots,money_left,reward");
        shots = Open(prefix + "_shots.csv",
            "label,episode,shot,fire_time,fire_time_fraction,aim_angle,waited,aim_predicted,predicted_value,predicted_distance,outcome,value,weight,retrieve_time,money_after");
    }

    static StreamWriter Open(string path, string header)
    {
        var writer = new StreamWriter(path, false) { AutoFlush = true }; // Nothing lost if the process is stopped
        writer.WriteLine(header);
        return writer;
    }

    public void WriteShot(Shot s)
    {
        shots.WriteLine(string.Join(",", label, s.episode, s.index, F(s.fireTime), F(s.fireFraction), F(s.aimAngle), F(s.waited),
            s.predicted, F(s.predictedValue), F(s.predictedDistance), s.outcome, F(s.value), F(s.weight), F(s.retrieveTime), F(s.moneyAfter)));
    }

    public void WriteEpisode(int episode, float target, float timeLimit, float money, bool caught, float catchTime,
                             int shotCount, int emptyShots, float moneyLeft, float reward)
    {
        episodes.WriteLine(string.Join(",", label, episode, F(target), F(timeLimit), F(money), money >= target ? 1 : 0, caught ? 1 : 0,
            caught ? F(catchTime) : "", caught ? F(catchTime / timeLimit) : "", shotCount, emptyShots, F(moneyLeft), F(reward)));
    }

    public void Close()
    {
        episodes.Dispose();
        shots.Dispose();
    }

    static string F(float value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
