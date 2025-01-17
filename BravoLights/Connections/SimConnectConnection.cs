﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BravoLights.Ast;
using BravoLights.Common;
using Microsoft.FlightSimulator.SimConnect;

namespace BravoLights.Connections
{       
    enum DefineId : uint
    {
        WASMRequestResponse = 1,
        WASMLVars = 2,

        // The start of dynamically-allocated definitions for simulator variables
        DynamicStart = 100
    }

    enum RequestId : uint
    {
        SimState = 1,
        WASMResponse = 2,
        WASMLVars = 3,
        AircraftLoaded = 4,
        FlightLoaded = 5,

        // The start of dynamically-allocated request ids for simulator variables
        DynamicStart = DefineId.DynamicStart
    }

    enum WASMReaderState
    {
        Neutral,
        ReadingLVars
    }

    class SimConnectConnection : IConnection, IWASMChannel
    {
        // Names of the client data areas established by the WASM module
        private const string CDA_NAME_SIMVAR = "BetterBravoLights.LVars";
        private const string CDA_NAME_REQUEST = "BetterBravoLights.Request";
        private const string CDA_NAME_RESPONSE = "BetterBravoLights.Response";

        // Ids for the client data areas
        private enum ClientDataId
        {
            LVars = 0,
            Request = 1,
            Response = 2
        }

        private const string RESPONSE_LVAR_START = "!LVARS-START";
        private const string RESPONSE_LVAR_END = "!LVARS-END";

        // Size of the request and response CDAs
        private const int RequestResponseSize = 256;

        [StructLayout(LayoutKind.Sequential)]
        public struct RequestString
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = RequestResponseSize)]
            public byte[] data;

            public RequestString(string str)
            {
                var bytes = Encoding.ASCII.GetBytes(str);
                var ret = new byte[RequestResponseSize];
                Array.Copy(bytes, ret, bytes.Length);
                data = ret;
            }
        }

        public struct ResponseString
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RequestResponseSize)]
            public string Data;
        }

        // This MUST match the value in the WASM
        private const int MaxDataValuesInPacket = 10;

        public struct LVarData : ILVarData
        {
            [MarshalAs(UnmanagedType.U2)]
            public short ValueCount;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxDataValuesInPacket, ArraySubType = UnmanagedType.U2)]
            public short[] Ids;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxDataValuesInPacket, ArraySubType = UnmanagedType.R8)]
            public double[] Values;

            short ILVarData.ValueCount => ValueCount;

            short[] ILVarData.Ids => Ids;

            double[] ILVarData.Values => Values;
        }

        private readonly uint LVarDataSize = (uint)Marshal.SizeOf<LVarData>();

        public static SimConnectConnection Connection = new();

        private Timer reconnectTimer = null;
        private SimState simState = SimState.SimExited;

        private SimConnectConnection()
        {
            LVarManager.Connection.SetWASMChannel(this);
        }

        public void Start()
        {
            ConnectNow();
        }

        private uint nextVariableId = (uint)DefineId.DynamicStart;

        private readonly Dictionary<uint, NameAndUnits> idToName = new();
        private readonly Dictionary<NameAndUnits, uint> nameToId = new(new NameAndUnitsComparer());
        private readonly Dictionary<NameAndUnits, EventHandler<ValueChangedEventArgs>> variableHandlers =
            new(new NameAndUnitsComparer());
        private readonly Dictionary<NameAndUnits, double> lastReportedValue = new(new NameAndUnitsComparer());

        private void SubscribeToSimConnect(NameAndUnits nameAndUnits)
        {
            nameToId.TryGetValue(nameAndUnits, out var id);

            // Do we already have a data definition and structure for this variable?
            if (id == 0)
            {
                // No. Create one.
                id = ++nextVariableId;

                idToName[id] = nameAndUnits;
                nameToId[nameAndUnits] = id;

                simconnect.AddToDataDefinition((DefineId)id, nameAndUnits.Name, nameAndUnits.Units, SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                simconnect.RegisterDataDefineStruct<ContainerStruct>((DefineId)id);
            }

            simconnect.RequestDataOnSimObject((RequestId)id, (DefineId)id, 0, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
        }

        private void UnsubscribeFromSimConnect(SimVarExpression simvar)
        {
            var name = simvar.NameAndUnits;

            var id = nameToId[name];

            // We're unsubscribing from this variable, so any value is going to be out of date, so remove the old value
            lastReportedValue.Remove(name);

            // Note: some web pages say that the way to unsubscribe is to call ClearClientDataDefinition. That doesn't appear to be correct.
            // Instead we use the (otherwise useless) 'NEVER' period
            simconnect.RequestDataOnSimObject((RequestId)id, (DefineId)id, 0, SIMCONNECT_PERIOD.NEVER, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }

        public void AddListener(IVariable variable, EventHandler<ValueChangedEventArgs> handler)
        {
            var simvar = (SimVarExpression)variable;
            var nau = simvar.NameAndUnits;

            lock (this)
            {
                variableHandlers.TryGetValue(nau, out var existingHandlers);
                var newHandlers = (EventHandler<ValueChangedEventArgs>)Delegate.Combine(existingHandlers, handler);
                variableHandlers[nau] = newHandlers;

                if (existingHandlers == null)
                {
                    if (this.simconnect != null)
                    {
                        SubscribeToSimConnect(nau);
                    }
                    else if (reconnectTimer == null)
                    {
                        ConnectNow();
                    }
                }

                SendLastValue(nau, handler);
            }
        }

        private void SendLastValue(NameAndUnits variable, EventHandler<ValueChangedEventArgs> handler)
        {
            if (simState == SimState.SimRunning)
            {
                if (lastReportedValue.TryGetValue(variable, out double lastValue))
                {
                    handler(this, new ValueChangedEventArgs { NewValue = lastValue });
                }
                else
                {
                    VariableHandlerUtils.SendNoValueError(this, handler);
                }
            }
            else
            {
                VariableHandlerUtils.SendNoConnectionError(this, handler);
            }
        }


        public void RemoveListener(IVariable variable, EventHandler<ValueChangedEventArgs> handler)
        {
            var simvar = (SimVarExpression)variable;
            var nau = simvar.NameAndUnits;

            lock (this)
            {
                variableHandlers.TryGetValue(nau, out var existingHandlers);
                var newListeners = (EventHandler<ValueChangedEventArgs>)Delegate.Remove(existingHandlers, handler);

                if (newListeners == null)
                {
                    variableHandlers.Remove(nau);

                    if (this.simconnect != null)
                    {
                        UnsubscribeFromSimConnect(simvar);
                    }
                }
                else
                {
                    variableHandlers[nau] = newListeners;
                }
            }
        }

        private const int WM_USER_SIMCONNECT = 0x0402;

        private SimConnect simconnect;

        public void ReceiveMessage()
        {
            if (simconnect != null)
            {
                try
                {
                    simconnect.ReceiveMessage();
                } catch (Exception)
                {
                    // Just squash. Sim's probably about to exit.
                }
            }
        }

        private void ReconnectTimerElapsed(object sender)
        {
            ConnectNow();
        }

        private void ConnectNow()
        {
            if (reconnectTimer != null)
            {
                reconnectTimer.Dispose();
                reconnectTimer = null;
            }

            if (simconnect != null)
            {
                return;
            }

            try
            {
                simconnect = new SimConnect("BravoLights", HWnd, WM_USER_SIMCONNECT, null, 0);
                simconnect.OnRecvSimobjectData += Simconnect_OnRecvSimobjectData;
                simconnect.OnRecvQuit += Simconnect_OnRecvQuit;

                simconnect.OnRecvEvent += Simconnect_OnRecvEvent;
                simconnect.SubscribeToSystemEvent(RequestId.SimState, "Sim");
                simconnect.SubscribeToSystemEvent(RequestId.AircraftLoaded, "AircraftLoaded");
                simconnect.SubscribeToSystemEvent(RequestId.FlightLoaded, "FlightLoaded");

                simconnect.OnRecvSystemState += Simconnect_OnRecvSystemState;

                ConfigureWASMComms();
                SendLVarRequest("CLEAR");

                RequestAircraftAndFlightStatus();

                RegisterCurrentVariables();
            }
            catch (Exception)
            {
                reconnectTimer = new(ReconnectTimerElapsed, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

                if (simState == SimState.SimRunning)
                {
                    RaiseSimStateChanged(SimState.SimStopped);
                }
            }
        }

        private void ConfigureWASMComms()
        {
            simconnect.MapClientDataNameToID(CDA_NAME_SIMVAR, ClientDataId.LVars);
            simconnect.CreateClientData(ClientDataId.LVars, (uint)Marshal.SizeOf<LVarData>(), SIMCONNECT_CREATE_CLIENT_DATA_FLAG.DEFAULT);

            simconnect.MapClientDataNameToID(CDA_NAME_REQUEST, ClientDataId.Request);
            simconnect.CreateClientData(ClientDataId.Request, 256, SIMCONNECT_CREATE_CLIENT_DATA_FLAG.DEFAULT);

            simconnect.MapClientDataNameToID(CDA_NAME_RESPONSE, ClientDataId.Response);
            simconnect.CreateClientData(ClientDataId.Response, 256, SIMCONNECT_CREATE_CLIENT_DATA_FLAG.DEFAULT);

            simconnect.AddToClientDataDefinition(DefineId.WASMRequestResponse, 0, RequestResponseSize, 0, 0);
            simconnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, ResponseString>(DefineId.WASMRequestResponse);
            simconnect.RequestClientData(
                ClientDataId.Response,
                RequestId.WASMResponse,
                DefineId.WASMRequestResponse,
                SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT,
                0,
                0,
                0
            );

            simconnect.AddToClientDataDefinition(DefineId.WASMLVars, 0, LVarDataSize, 0, 0);
            simconnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, LVarData>(DefineId.WASMLVars);
            simconnect.RequestClientData(
                ClientDataId.LVars,
                RequestId.WASMLVars,
                DefineId.WASMLVars,
                SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT,
                0,
                0,
                0
            );

            simconnect.OnRecvClientData += SimConnect_OnRecvClientData;
        }

        private void RequestAircraftAndFlightStatus()
        {
            simconnect.RequestSystemState(RequestId.AircraftLoaded, "AircraftLoaded");
            simconnect.RequestSystemState(RequestId.FlightLoaded, "FlightLoaded");
        }

        private void Simconnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            lock (this)
            {
                switch ((RequestId)data.uEventID)
                {
                    case RequestId.SimState:
#if DEBUG
                        Debug.WriteLine($"SimState {data.dwData}");
#endif
                        RaiseSimStateChanged(data.dwData == 1 ? SimState.SimRunning : SimState.SimStopped);
                        break;
                    case RequestId.AircraftLoaded:
                        {
                            var fileData = (SIMCONNECT_RECV_EVENT_FILENAME)data;
                            HandleAircraftChanged(fileData.szFileName);
                        }
                        break;
                    case RequestId.FlightLoaded:
                        {
                            var fileData = (SIMCONNECT_RECV_EVENT_FILENAME)data;
                            HandleFlightLoaded(fileData.szFileName);
                        }
                        break;
                }
            }
        }

        private void Simconnect_OnRecvSystemState(SimConnect sender, SIMCONNECT_RECV_SYSTEM_STATE data)
        {
            lock (this)
            {
                switch ((RequestId)data.dwRequestID)
                {
                    case RequestId.SimState:
                        RaiseSimStateChanged(data.dwInteger == 1 ? SimState.SimRunning : SimState.SimStopped);
                        break;
                    case RequestId.AircraftLoaded:
                        HandleAircraftChanged(data.szString);
                        break;
                    case RequestId.FlightLoaded:
                        HandleFlightLoaded(data.szString);
                        break;
                }
            }
        }

        private Timer periodicLVarTimer = null;

        private void PeriodicLVarTimerElapsed(object state)
        {
            Debug.WriteLine("PeriodicLVarTimerElapsed");
            ScheduleLVarCheck();
        }

        private void RaiseSimStateChanged(SimState state)
        {
            lock (this)
            {
                if (simState == state)
                {
                    // State is unchanged
                    return;
                }

                simState = state;

                if (periodicLVarTimer != null)
                {
                    periodicLVarTimer.Dispose();
                    periodicLVarTimer = null;
                }

                if (simState == SimState.SimRunning)
                {
                    periodicLVarTimer = new(PeriodicLVarTimerElapsed, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                    ScheduleLVarCheck();
                }

                if (simState == SimState.SimExited)
                {
                    // The sim has exited. We're likely to be exiting soon, but in
                    // case we don't, we should tidy up.

                    // Any previous values and subscriptions/definitions are now not valid.
                    idToName.Clear();
                    nameToId.Clear();
                    lastReportedValue.Clear();

                    lvarCheckTimer?.Dispose();
                    lvarCheckTimer = null;
                    periodicLVarTimer?.Dispose();
                    periodicLVarTimer = null;

                    // SimConnect has gone away; presumably MSFS has exited.
                    // BetterBravoLights itself might not be exiting (if it's being run without install),
                    // but we need to clean up these registrations.
                    this.idToName.Clear();
                    this.nameToId.Clear();
                    simconnect = null;

                    // Update all existing subscribers
                    foreach (var kvp in variableHandlers)
                    {
                        var variable = kvp.Key;
                        var handlers = kvp.Value;

                        SendLastValue(variable, handlers);
                    }
                }
                else
                {
                    // Despite subscribing to AircraftLoaded and FlightLoaded events, we don't actually seem
                    // to receive them, so we explicitly ask for them when we get SimStart/SimStop events.
                    RequestAircraftAndFlightStatus();
                }

                OnSimStateChanged?.Invoke(this, new SimStateEventArgs { SimState = state });
            }
        }

        private static readonly Regex AircraftPathRegex = new("Airplanes\\\\(.*)\\\\");

        private void HandleAircraftChanged(string aircraftPath)
        {
            if (OnAircraftLoaded != null)
            {
                // aircraftPath is something like
                // SimObjects\\Airplanes\\Asobo_C172SP_Classic\\aircraft.CFG
                // We just want 'Asobo_C172SP_Classic'
                var match = AircraftPathRegex.Match(aircraftPath);
                if (match.Success)
                {
                    var aircraftName = match.Groups[1].Value;
                    Debug.WriteLine($"HandleAircraftChanged {aircraftName}. Checking LVars");

                    // Note: LVars are not registered by an aircraft until a little while _after_ it has loaded.
                    // So the first time we get an AircraftChanged event, the lvars will not be present.
                    // However, we request aircraft + flight information on each SimStart/SimStop, which should catch them.
                    ScheduleLVarCheck();
                    OnAircraftLoaded(this, new AircraftEventArgs { Aircraft = aircraftName });
                }
            }
        }

        private void HandleFlightLoaded(string flightPath)
        {
            // Examples:
            // flights\other\MainMenu.FLT
            // C:\Users\royston\AppData\Local\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\MISSIONS\ACTIVITIES\ASOBO-BUSHTRIP-FINLAND_SAVE\ASOBO-BUSHTRIP-FINLAND_SAVE\ASOBO-BUSHTRIP-FINLAND_SAVE.FLT
            // C:\Users\royston\AppData\Local\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\MISSIONS\ACTIVITIES\ASOBO-BUSHTRIP-FINLAND_SAVE\AUTOSAVE\\BCKP\\ASOBO-BUSHTRIP-FINLAND_SAVE.FLT
            // missions\Asobo\Tutorials\VFRNavigation\LandmarkNavigation\03_Training_LandmarkNavigation.FLT

            InMainMenu = flightPath.EndsWith("flights\\other\\mainmenu.flt", StringComparison.InvariantCultureIgnoreCase);
            Debug.WriteLine($"HandleFlightLoaded. {flightPath}. Checking LVars");
            ScheduleLVarCheck();
        }

        /// <summary>
        /// Called when we receive some event that suggests that the simulator LVars may have changed, e.g. aircraft/flight change/simstart/simstop
        /// </summary>
        private void ScheduleLVarCheck()
        {
#if DEBUG
            Debug.WriteLine("ScheduleLVarCheck");
#endif

            if (lvarCheckTimer == null)
            {
                lvarCheckTimer = new(LVarCheckTimerElapsed, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            }
        }

        private Timer lvarCheckTimer = null;

        /// <summary>
        /// Called every ~30s or so, just to check if new lvars have appeared. Some aircraft take a while to register theirs.
        /// </summary>
        private void LVarCheckTimerElapsed(object state)
        {
#if DEBUG
            Debug.WriteLine("LVarCheckTimerElapsed");
#endif
            lvarCheckTimer.Dispose();
            lvarCheckTimer = null;

            CheckForNewLVars();
        }

        private bool hasEverCheckedForLVars = false;

        /// <summary>
        /// Asks the WASM module to check for new LVars.
        /// </summary>
        private void CheckForNewLVars()
        {
#if DEBUG
            Debug.WriteLine("CheckForNewLVars");
#endif
            if (hasEverCheckedForLVars)
            {
                // Ask the WASM module to check for new lvars
                SendLVarRequest($"CHECKLVARS");
            } else
            {
                hasEverCheckedForLVars = true;
                // Ask the WASM module for ALL lvars
                SendLVarRequest($"LISTLVARS");
            }
        }

        public event EventHandler OnInMainMenuChanged;
        
        private bool inMainMenu = true;
        public bool InMainMenu
        {
            get {
                return inMainMenu;
            }
            private set
            {
                if (inMainMenu == value)
                {
                    return;
                }

                CheckForNewLVars();
                inMainMenu = value;
                OnInMainMenuChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Called when MSFS quits
        private void Simconnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            RaiseSimStateChanged(SimState.SimExited);
        }

        private void RegisterCurrentVariables()
        {
            foreach (var nau in this.variableHandlers.Keys)
            {
                SubscribeToSimConnect(nau);
            }
        }

        private List<string> incomingLVars = new();

        private WASMReaderState readerState = WASMReaderState.Neutral;

        private void SimConnect_OnRecvClientData(SimConnect sender, SIMCONNECT_RECV_CLIENT_DATA data)
        {
            switch ((RequestId)data.dwRequestID)
            {
                case RequestId.WASMResponse:
                    {
                        var responseString = ((ResponseString)data.dwData[0]).Data;

                        switch (responseString)
                        {
                            case RESPONSE_LVAR_START:
                                readerState = WASMReaderState.ReadingLVars;
                                return;
                            case RESPONSE_LVAR_END:
                                readerState = WASMReaderState.Neutral;
                                var newLVars = incomingLVars;
                                incomingLVars = new();
                                LVarManager.Connection.UpdateLVarList(newLVars);
                                return;
                        }

                        if (readerState == WASMReaderState.ReadingLVars)
                        {
                            incomingLVars.Add(responseString);
                            return;
                        }
                    }
                    break;
                case RequestId.WASMLVars:
                    { 
                        var lvarUpdate = ((LVarData)data.dwData[0]);
                        LVarManager.Connection.UpdateLVarValues(lvarUpdate);
                    }
                    break;
            }
        }

        private void Simconnect_OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            lock (this)
            {
                if (idToName.TryGetValue(data.dwRequestID, out NameAndUnits nau))
                {
                    var dataContainer = data.dwData[0] as ContainerStruct?;
                    var newValue = dataContainer.Value.doubleValue;

                    variableHandlers.TryGetValue(nau, out var handlers);

                    if (handlers != null)
                    {
                        lastReportedValue[nau] = newValue;
                        var e = new ValueChangedEventArgs { NewValue = newValue };
                        handlers(this, e);
                    }
                }
            }
        }

        public static IntPtr HWnd { get; set; }

        public event EventHandler<AircraftEventArgs> OnAircraftLoaded;
        public SimState SimState
        {
            get { return simState; }
        }
        public event EventHandler<SimStateEventArgs> OnSimStateChanged;

        private void SendLVarRequest(string message)
        {
#if DEBUG
            Debug.WriteLine($"Sending LVarRequest {message}");
#endif

            var cmd = new RequestString(message);
            try
            {
                simconnect.SetClientData(
                    ClientDataId.Request,
                    DefineId.WASMRequestResponse,
                    SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT,
                    0,
                    cmd
                );
            }
            catch (Exception)
            {
                RaiseSimStateChanged(SimState.SimExited);
            }
        }

        void IWASMChannel.ClearSubscriptions()
        {
            var message = $"CLEAR";
            SendLVarRequest(message);
        }

        void IWASMChannel.Subscribe(short id)
        {
            var message = $"SUBSCRIBE {id.ToString(CultureInfo.InvariantCulture)}";
            SendLVarRequest(message);
        }

        void IWASMChannel.Unsubscribe(short id)
        {
            var message = $"UNSUBSCRIBE {id.ToString(CultureInfo.InvariantCulture)}";
            SendLVarRequest(message);
        }
    }

    public enum SimState
    {
        SimRunning,
        SimStopped,
        SimExited
    }

    class NameAndUnits
    {
        public string Name;
        public string Units;

        public override string ToString()
        {
            return $"{Name}, {Units}";
        }
    }

    class NameAndUnitsComparer : IEqualityComparer<NameAndUnits>
    {
        public bool Equals(NameAndUnits x, NameAndUnits y)
        {
            return x.Name == y.Name && x.Units == y.Units;
        }

        public int GetHashCode([DisallowNull] NameAndUnits obj)
        {
            return obj.Name.GetHashCode();
        }
    }

    public class SimStateEventArgs: EventArgs
    {
        public SimState SimState;
    }

    class AircraftEventArgs : EventArgs
    {
        public string Aircraft;
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Ansi, Pack = 1)]
    struct ContainerStruct
    {
        [FieldOffset(0)]
        public double doubleValue;
    }
}
