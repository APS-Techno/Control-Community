using System.Xml.Linq;
using EmulatorCore.Models;

namespace EmulatorCore.Interfaces;

public sealed record DeviceCreateContext(XElement Element, string SectionName, string ShortType, string Host, PhysicsConfig Physics);
