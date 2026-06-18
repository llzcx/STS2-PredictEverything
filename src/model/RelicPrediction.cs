using Godot;
using MegaCrit.Sts2.Core.Models;

namespace PredictEverything;

public record RelicPrediction(string Name, string RarityLabel, Texture2D? Icon, RelicModel? Relic);
