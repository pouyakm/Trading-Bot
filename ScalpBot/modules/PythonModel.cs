using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ScalpBot.modules;

public class PythonModel
{
    private readonly string _pythonExe;
    private readonly string _scriptPath;
    public PythonModel(string pythonExe = "python", string scriptPath = "LSTM_Model/train_and_predict.py")
    {
        _pythonExe = pythonExe;
        _scriptPath = scriptPath;
    }

    public async Task<bool> TrainIfNeededAsync(string dataCsvPath, string modelPath)
    {
        if (File.Exists(modelPath)) return true;
        Console.WriteLine($"[ML] training model for {Path.GetFileName(modelPath)} ...");
        var psi = new ProcessStartInfo
        {
            FileName = _pythonExe,
            Arguments = $"\"{_scriptPath}\" train \"{dataCsvPath}\" \"{modelPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        string stdout = await proc.StandardOutput.ReadToEndAsync();
        string stderr = await proc.StandardError.ReadToEndAsync();
        proc.WaitForExit(600000);
        if (!string.IsNullOrWhiteSpace(stderr)) ;// Console.WriteLine("[ML stderr] " + stderr);
        return stdout.Contains("\"trained\": true") || stdout.ToLower().Contains("trained");
    }

    public async Task<decimal> PredictAsync(string dataCsvPath, string modelPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _pythonExe,
            Arguments = $"\"{_scriptPath}\" predict \"{dataCsvPath}\" \"{modelPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        string stdout = await proc.StandardOutput.ReadToEndAsync();
        string stderr = await proc.StandardError.ReadToEndAsync();
        proc.WaitForExit(10000);
        if (!string.IsNullOrWhiteSpace(stderr)) ;// Console.WriteLine("[ML stderr] " + stderr);
        try
        {
            var jo = JObject.Parse(stdout);
            return jo["prob_up"]?.Value<decimal>() ?? 0.5m;
        }
        catch
        {
            Console.WriteLine("[ML] parse failed, returning 0.5");
            return 0.5m;
        }
    }
}
