﻿using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;

namespace BravoLights.Installation
{
    static class Installer
    {
        private static readonly string AFCBridgeAddonName = "AFCBridge";
        private static readonly string BravoLightsAddonName = "BravoLights";

        static Installer()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private static string ExeXmlPath
        {
            get
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var localCache = Path.Join(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache");

                var exeXmlPath = Path.Join(localCache, "exe.xml");

                if (!File.Exists(exeXmlPath))
                {
                    throw new Exception($"Could not locate exe.xml file. Tried {exeXmlPath}");
                }

                return exeXmlPath;
            }
        }

        public static bool IsSetToRunOnStartup
        {
            get
            {
                try
                {
                    var xdoc = XDocument.Load(ExeXmlPath);
                    var lightsEl = FindAddon(xdoc, BravoLightsAddonName);
                    if (lightsEl != null)
                    {
                        return lightsEl.Element("Disabled").Value == "False";
                    }
                }
                catch
                {
                    // If we can't even find/read the exe.xml file, it's not going to be set to run on startup
                }

                return false;
            }
        }

        public static string Install()
        {
            var xdoc = XDocument.Load(ExeXmlPath);

            var afcBridgeEl = FindAddon(xdoc, AFCBridgeAddonName);
            if (afcBridgeEl != null)
            {
                afcBridgeEl.SetElementValue("Disabled", "True");
            }

            var lightsEl = FindAddon(xdoc, BravoLightsAddonName);
            if (lightsEl == null)
            {
                lightsEl = new XElement("Launch.Addon", new XElement("Name", BravoLightsAddonName));
                xdoc.Root.Add(lightsEl);
            }
            lightsEl.SetElementValue("Disabled", "False");
            lightsEl.SetElementValue("Path", Path.Join(Application.StartupPath, "BetterBravoLights.exe"));

            xdoc.Save(ExeXmlPath);

            if (afcBridgeEl == null)
            {
                return "Better Bravo Lights will now automatically start up with MSFS.";
            } else
            {
                return "Better Bravo Lights will now automatically start up with MSFS instead of the AFCBridge.";
            }
        }

        public static string Uninstall()
        {
            var xdoc = XDocument.Load(ExeXmlPath);

            var afcBridgeEl = FindAddon(xdoc, AFCBridgeAddonName);
            if (afcBridgeEl != null)
            {
                afcBridgeEl.SetElementValue("Disabled", "False");
            }

            var lightsEl = FindAddon(xdoc, BravoLightsAddonName);
            if (lightsEl != null)
            {
                lightsEl.Remove();
            }

            xdoc.Save(ExeXmlPath);

            if (afcBridgeEl == null)
            {
                return "Better Bravo Lights removed.";
            }
            else
            {
                return "Better Bravo Lights removed. AFCBridge will now be used instead.";
            }
        }

        private static XElement FindAddon(XDocument document, string name)
        {
            foreach (var addonEl in document.Element("SimBase.Document").Elements("Launch.Addon"))
            {
                var addonName = addonEl.Element("Name").Value;
                if (addonName == name)
                {
                    return addonEl;
                }
            }
            return null;
        }
    }
}