using System.Drawing;
using System.Text.Json.Serialization;
using Glimpse.API;

namespace Glimpse.Configs;

[method: JsonConstructor]
public struct StateConfig() : IConfig
{
    public const string ConfigName = "LastState";

    public Point Position;

    public Size Size;

    public bool Maximized;
}