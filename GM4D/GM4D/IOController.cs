﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
/* 
 * Filename: IOController.cs
 * Author: Dennis Stodko
 * Date: 2015
 * Description: handles all IO connections and actions
 */
namespace GM4D
{
    /// <summary>
    /// controller for IO operations
    /// </summary>
    class IOController
    {
        /// <summary>
        /// raised if OS changes or is detected
        /// </summary>
        public event EventHandler OsIsUnixChanged;
        private bool osIsUnix;
        /// <summary>
        /// true if OS is Unix type
        /// </summary>
        public bool OsIsUnix
        {
            get
            {
                return this.osIsUnix;
            }
            set
            {
                this.osIsUnix = value;
                OsIsUnixChanged(this, new EventArgs());
            }
        }
        /// <summary>
        /// raised if user is detected/changes
        /// </summary>
        public event EventHandler UserIsSUChanged;
        private bool userIsSU;
        /// <summary>
        /// true if the user has SU privilleges
        /// </summary>
        public bool UserIsSU 
        {
            get
            {
                return this.userIsSU;
            }
            set
            {
                this.userIsSU = value;
                UserIsSUChanged(this, new EventArgs());
            } 
        }
        /// <summary>
        /// Settings object (data storage)
        /// </summary>
        private Settings settings;
        /// <summary>
        /// process for bash
        /// </summary>
        private Process shellProc;
        /// <summary>
        /// ProcessStartInfo for the bash, pass shell command to execute in Arguments
        /// </summary>
        private ProcessStartInfo shellStartInfo;
        private delegate void SaveSettingsToFileDelegate(string filename);
        private delegate ArrayList ReadDhcpdLeasesFileDelegate(string filename);
        private delegate ArrayList ReadConfigFileDelegate(string filename);
        /// <summary>
        /// raised when configuration file was loaded
        /// </summary>
        public event EventHandler SettingsFileLoadedEvt;
        /// <summary>
        /// controller for IO operations
        /// </summary>
        public IOController(Settings _settings)
        {
            this.settings = _settings;
        }
        /// <summary>
        /// initialises a bash process in shellProc
        /// </summary>
        public void InitShell()
        {
            IOController.Log(this, "OS: " + Environment.OSVersion.ToString(), Flag.status);
            if (Environment.OSVersion.ToString().Contains("Unix"))
            {
                this.OsIsUnix = true;
                // initialize bash process
                this.shellProc = new Process();
                this.shellStartInfo = new ProcessStartInfo();
                this.shellStartInfo.FileName = "/bin/bash";
                this.shellStartInfo.UseShellExecute = false;
                this.shellStartInfo.RedirectStandardOutput = true;
                this.shellStartInfo.Arguments = "-c \"whoami\"";
                this.shellProc.StartInfo = this.shellStartInfo;
                this.shellProc.Start();
                // check if application was started with su privilleges
                string username = this.shellProc.StandardOutput.ReadToEnd();
                username = Regex.Replace(username, @"\s+", "");
                if (username == "root")
                {
                    this.UserIsSU = true;
                }
                else
                {
                    this.UserIsSU = false;
                }
            }
            else
            {
                this.OsIsUnix = false;
            }
        }
        /// <summary>
        /// saves the DHCP configuration to a file
        /// </summary>
        /// <param name="filename">path and filname as string</param>
        public void SaveSettingsFile(String filename)
        {
            // create a new delegate
            SaveSettingsToFileDelegate saveSettingsToFileDelegate = null;
            // assign the writeSettingsToFile function to the delegate
            saveSettingsToFileDelegate = new SaveSettingsToFileDelegate(writeSettingsToFile);
            // assign callback function and start async process
            IAsyncResult saveSettingsToFileResult = saveSettingsToFileDelegate.BeginInvoke(filename, SaveSettingsToFileComplete, null);
        }
        /// <summary>
        /// the process of writing the config to a file
        /// </summary>
        /// <param name="filename">path and filename as string</param>
        private void writeSettingsToFile(String filename)
        {
            System.IO.StreamWriter streamWriter = new System.IO.StreamWriter(filename);
            streamWriter.Write(CreateConfig());
            streamWriter.Flush();
            streamWriter.Close();
        }
        /// <summary>
        /// creates the dhcpd.conf code from the data in the settings object
        /// </summary>
        /// <returns>config code as string</returns>
        public String CreateConfig()
        {
            String dhcpConfig = "#dhcpd.conf created by GM4D tool" + Environment.NewLine +
                "one-lease-per-client true;" + Environment.NewLine +
                "update-static-leases true;" + Environment.NewLine +
                "default-lease-time " + settings.DefaultLeaseTime + ";" + Environment.NewLine +
                "max-lease-time " + settings.MaxLeaseTime + ";" + Environment.NewLine;
            if (settings.HostSubnetMaskIsSet)
            {
                dhcpConfig += "option subnet-mask " + settings.HostSubnetMask + ";" + Environment.NewLine;
            }
            if (settings.SubnetIsSet && settings.SubnetMaskIsSet)
            {
                dhcpConfig += "subnet " + settings.Subnet + " netmask " + settings.SubnetMask + " {" + Environment.NewLine +
                "   range " + settings.IpRangeStart + " " + settings.IpRangeEnd + ";" + Environment.NewLine;
                if (settings.GatewayIsSet)
                {
                    dhcpConfig += "   option routers " + settings.Gateway + ";" + Environment.NewLine;
                }
                if (settings.PrimaryDNSIsSet)
                {
                    dhcpConfig += "   option domain-name-servers " + settings.PrimaryDNS;
                    if (settings.SecondaryDNSIsSet)
                    {
                        dhcpConfig += ", " + settings.SecondaryDNS;
                    }
                    dhcpConfig += ";" + Environment.NewLine;
                }
                if (settings.GetStaticLeases().Count >= 1)
                {
                    foreach (KeyValuePair<String, StaticLease> entry in settings.GetStaticLeases())
                    {
                        dhcpConfig += "   host " + entry.Value.DeviceName + " {" + Environment.NewLine +
                            "      hardware ethernet " + entry.Value.MACAddress + ";" + Environment.NewLine +
                            "      fixed-address " + entry.Value.IPAddress + ";" + Environment.NewLine +
                            "   }" + Environment.NewLine;
                    }
                }
                dhcpConfig += "}";
            }
            return dhcpConfig;
        }
        /// <summary>
        /// is called when settings file save is complete
        /// </summary>
        /// <param name="result"></param>
        public void SaveSettingsToFileComplete(IAsyncResult result)
        {
            IOController.Log(this, "File saved", Flag.status);
        }
        /// <summary>
        /// loads DHCP setting from file with filename
        /// </summary>
        /// <param name="filename">path and filename as string</param>
        public void LoadSettingsFile(String filename)
        {
            if (File.Exists(filename))
            {
                IOController.Log(this, "LoadSettingsFile filename: " + filename, Flag.status);
                ReadConfigFileDelegate readConfigFileDelegate = new ReadConfigFileDelegate(ProcessConfigFile);
                IAsyncResult readConfigFileDelegateResult = readConfigFileDelegate.BeginInvoke(filename, parseConfig, null);
            }
            else
            {
                throw new FileNotFoundException(filename + " not found.");
            }
        }
        /// <summary>
        /// takes a filename, reads in the text content of file and returns contents as ArrayList of lines
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>filecontent as string</returns>
        private ArrayList ProcessConfigFile(string filename)
        {
            IOController.Log(this, "ProcessConfigFile entered", IOController.Flag.debug);
            IOController.Log(this, "ProcessConfigFile filename: " + filename, Flag.status);
            if (File.Exists(filename))
            {
                using (StreamReader sr = File.OpenText(filename))
                {
                    string input;
                    ArrayList filecontent = new ArrayList();
                    while ((input = sr.ReadLine()) != null)
                    {
                        filecontent.Add(input);
                    }
                    return filecontent;
                }
            }
            else
            {
                throw new FileNotFoundException(filename + " not found.");
            }
        }
        /// <summary>
        /// takes the filecontent from IAsyncResult und parses DHCP settings to the settings object
        /// </summary>
        /// <param name="result">takes an IAsyncResult containing the file content as string</param>
        public void parseConfig(IAsyncResult result)
        {
            AsyncResult aResult = (AsyncResult)result;
            ReadConfigFileDelegate readConfigFileDelegate = (ReadConfigFileDelegate)aResult.AsyncDelegate;
            ArrayList filecontent = readConfigFileDelegate.EndInvoke(result);
            IOController.Log(this, "parseConfig filecontent: " + String.Join(",", filecontent), Flag.debug);
            StaticLease staticLease = null;
            this.settings.StaticLeases.Clear();
            foreach (string line in filecontent)
            {
                // clean line from tabs, break, whitespaces etc.
                string cleanline = Regex.Replace(line, @"\s+", " ");
                cleanline = cleanline.Trim();
                // check for valid tags
                if (cleanline.StartsWith("default-lease-time"))
                {
                    int endindex = cleanline.IndexOf(";");
                    int startindex = 19;
                    int tmp;
                    if (int.TryParse(cleanline.Substring(startindex, endindex - startindex), out tmp))
                    {
                        this.settings.DefaultLeaseTime = tmp;
                    }
                }
                else if (cleanline.StartsWith("max-lease-time"))
                {
                    int endindex = cleanline.IndexOf(";");
                    int startindex = 15;
                    int tmp;
                    if (int.TryParse(cleanline.Substring(startindex, endindex - startindex), out tmp))
                    {
                        this.settings.MaxLeaseTime = tmp;
                    }
                }
                else if (cleanline.StartsWith("subnet"))
                {
                    string[] strArr = cleanline.Split(' ');
                    System.Net.IPAddress tmp;
                    if (strArr.Length > 1)
                    {
                        if (System.Net.IPAddress.TryParse(strArr[1], out tmp))
                        {
                            this.settings.Subnet = tmp.ToString();
                        }
                        if (strArr.Length > 3)
                        {
                            if (strArr[2].Contains("netmask"))
                            {
                                if (System.Net.IPAddress.TryParse(strArr[3], out tmp))
                                {
                                    this.settings.SubnetMask = tmp.ToString();
                                }
                            }
                        }
                    }
                }
                else if (cleanline.StartsWith("range"))
                {
                    string[] strArr = cleanline.Split(' ');
                    System.Net.IPAddress tmp;
                    if (strArr.Length > 1)
                    {
                        if (System.Net.IPAddress.TryParse(strArr[1], out tmp))
                        {
                            this.settings.IpRangeStart = tmp.ToString();
                        }
                    }
                    if (strArr.Length > 2)
                    {
                        strArr[2] = strArr[2].TrimEnd(';');
                        if (System.Net.IPAddress.TryParse(strArr[2], out tmp))
                        {
                            this.settings.IpRangeEnd = tmp.ToString();
                        }
                    }
                }
                else if (cleanline.StartsWith("option"))
                {
                    string[] strArr = cleanline.Split(' ');
                    System.Net.IPAddress tmpIp;
                    if (strArr.Length > 2)
                    {
                        switch (strArr[1])
                        {
                            case "routers":
                                strArr[2] = strArr[2].TrimEnd(';');
                                if (System.Net.IPAddress.TryParse(strArr[2], out tmpIp))
                                {
                                    this.settings.Gateway = tmpIp.ToString();
                                }
                                break;
                            case "domain-name-servers":
                                strArr[2] = strArr[2].TrimEnd(';');
                                strArr[2] = strArr[2].TrimEnd(',');
                                if (System.Net.IPAddress.TryParse(strArr[2], out tmpIp))
                                {
                                    this.settings.PrimaryDNS = tmpIp.ToString();
                                }
                                if (strArr.Length > 3)
                                {
                                    strArr[3] = strArr[3].TrimEnd(';');
                                    if (System.Net.IPAddress.TryParse(strArr[3], out tmpIp))
                                    {
                                        this.settings.SecondaryDNS = tmpIp.ToString();
                                    }
                                }
                                break;
                            default: break;
                        }
                    }
                }
                else if (cleanline.StartsWith("host"))
                {
                    staticLease = new StaticLease();
                    string[] strArr = cleanline.Split(' ');
                    if (strArr.Length > 1)
                    {
                        staticLease.DeviceName = strArr[1];
                    }
                }
                else if (cleanline.StartsWith("hardware ethernet"))
                {
                    int endindex = cleanline.IndexOf(";");
                    int startindex = 18;
                    staticLease.MACAddress = cleanline.Substring(startindex, endindex - startindex);
                }
                else if (cleanline.StartsWith("fixed-address"))
                {
                    int endindex = cleanline.IndexOf(";");
                    int startindex = 14;
                    staticLease.IPAddress = cleanline.Substring(startindex, endindex - startindex);
                }
                if (staticLease != null && cleanline.Contains("}"))
                {
                    staticLease.ID = (this.settings.GetStaticLeases().Count + 1).ToString();
                    this.settings.AddStaticLease(staticLease);
                    staticLease = null;
                }
            }
            if (SettingsFileLoadedEvt != null)
            {
                SettingsFileLoadedEvt(this, new EventArgs());
            }
        }
        /// <summary>
        /// sets a static host ip
        /// </summary>
        public void SetNewHostIp()
        {
            if (OsIsUnix)
            {
                SaveSettingsFile(Environment.CurrentDirectory.ToString() + "/dhcpd.gm4d");
                shellStartInfo.Arguments = string.Format("-c \"gksudo ifconfig " + ((HostNIC)this.settings.Interfaces[this.settings.SelectedInterfaceIndex]).Id + " " + this.settings.NewHostIP + " netmask " + this.settings.NewHostSubnetMask + "\"");
                shellProc.Start();
                IOController.Log(this, "SetNewHostIp " + shellProc.StandardOutput.ReadToEnd() , Flag.debug);
                shellProc.WaitForExit();
                GetHostInfo();
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// installes isc-dhcp-server package
        /// </summary>
        public void InstallDHCPServer()
        {
            if (OsIsUnix)
            {
                SaveSettingsFile(Environment.CurrentDirectory.ToString() + "/dhcpd.gm4d");
                shellStartInfo.Arguments = string.Format("-c \"gksudo apt-get install isc-dhcp-server\"");
                shellProc.Start();
                IOController.Log(this, "InstallDHCPServer " + shellProc.StandardOutput.ReadToEnd(), Flag.debug);
                shellProc.WaitForExit();
                GetDHCPServerInstallStatus();
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// saves the configuration and copies it to the /etc/dhcp/dhcpd.conf file. 
        /// Afterwards the interface in the /etc/default/isc-dhcp-server is updated and the dhcpd service is restarted.
        /// </summary>
        public void ApplySettingsToDHCPServer()
        {
            if (OsIsUnix)
            {
                SaveSettingsFile(Environment.CurrentDirectory.ToString() + "/dhcpd.gm4d");
                shellStartInfo.Arguments = string.Format("-c \"gksudo mv " + Environment.CurrentDirectory.ToString() + "/dhcpd.gm4d /etc/dhcp/dhcpd.conf\"");
                shellProc.Start();
                IOController.Log(this, "ApplySettingsToDHCPServer " + shellProc.StandardOutput.ReadToEnd(), Flag.debug);
                shellProc.WaitForExit();
                this.ApplySelectedInterface();
                if (this.settings.IsDHCPServerRunning)
                {
                    this.RestartDHCPServer();
                }
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// calls functions to load, modify and save /etc/default/isc-dhcp-server file
        /// </summary>
        public void ApplySelectedInterface()
        {
            LoadEtcDefaultConfigFile();
            SaveEtcDefaultConfigFile();
        }
        /// <summary>
        /// retrieves selected interface from /etc/default/isc-dhcp-server file
        /// </summary>
        public void GetSelectedInterfaceFromEtcDefault()
        {
            IOController.Log(this, "GetSelectedInterfaceFromEtcDeafult enter", Flag.debug);
            this.settings.SelectInterface(0);
            if (OsIsUnix)
            {
                if (this.settings.IsDHCPServerInstalled)
                {
                    if (File.Exists("/etc/default/isc-dhcp-server"))
                    {
                        ArrayList filecontent = ProcessConfigFile("/etc/default/isc-dhcp-server");
                        IOController.Log(this, "ProcessConfigFile returned: filecontent: " + String.Join(",", filecontent), Flag.debug);
                        foreach (string line in filecontent)
                        {
                            string trimmedline = line.Trim();
                            if (trimmedline.StartsWith("INTERFACES"))
                            {
                                IOController.Log(this, "found INTERFACES entry " + trimmedline, IOController.Flag.debug);
                                if (trimmedline.Length > 13)
                                {
                                    string foundInterfaceId = trimmedline.Remove(0, 11);
                                    foundInterfaceId = foundInterfaceId.Trim('"');
                                    IOController.Log(this, "getting index for " + foundInterfaceId, Flag.debug);
                                    for (int i = 0; i < this.settings.Interfaces.Count; i++)
                                    {
                                        IOController.Log(this, ((HostNIC)this.settings.Interfaces[i]).Id + " ?= " + foundInterfaceId, Flag.debug);
                                        if (((HostNIC)this.settings.Interfaces[i]).Id == foundInterfaceId)
                                        {
                                            IOController.Log(this, "found matching interface id " + foundInterfaceId + " index " + i, Flag.status);
                                            this.settings.SelectInterface(i);
                                            break;
                                        }
                                    }
                                }

                            }
                        }
                    }
                    else
                    {
                        IOController.Log(this, "FileNotFoundException /etc/default/isc-dhcp-server", Flag.error);
                    }
                }
                else
                {
                    IOController.Log(this, "DHCP Server not installed", Flag.error);
                }
            }
        }
        /// <summary>
        /// loads /etc/default/isc-dhcp-server file
        /// </summary>
        public void LoadEtcDefaultConfigFile()
        {
            this.newEtcDefaultConfig = null;
            IOController.Log(this, "LoadEtcDefaultConfigFile enter", Flag.debug);
            if (OsIsUnix)
            {
                if (this.settings.IsDHCPServerInstalled)
                {
                    if (File.Exists("/etc/default/isc-dhcp-server"))
                    {
                        IOController.Log(this, "LoadEtcDefaultConfig", Flag.debug);
                        ReadConfigFileDelegate readConfigFileDelegate = new ReadConfigFileDelegate(ProcessConfigFile);
                        IAsyncResult readConfigFileDelegateResult = readConfigFileDelegate.BeginInvoke("/etc/default/isc-dhcp-server", processEtcDefaultConfigFile, null);
                    }
                    else
                    {
                        IOController.Log(this, "FileNotFoundException /etc/default/isc-dhcp-server", Flag.error);
                        throw new FileNotFoundException("/etc/default/isc-dhcp-server" + " not found.");
                    }
                }
                else
                {
                    IOController.Log(this, "DHCP Server not installed", Flag.error);
                    throw new System.Exception("DHCP Server not installed");
                }
            }
            else
            {
                IOController.Log(this, "System in not a Unix environment", Flag.error);
                throw new System.Exception("System in not a Unix environment");
            }
        }
        /// <summary>
        /// arraylist to store lines of code for /etc/default/isc-dhcp-server
        /// </summary>
        private ArrayList newEtcDefaultConfig;
        /// <summary>
        /// edites the config of /etc/default/isc-dhcp-server to set the seletc interface
        /// </summary>
        /// <param name="ar"></param>
        private void processEtcDefaultConfigFile(IAsyncResult ar)
        {
            IOController.Log(this, "processEtcDefaultConfigFile entered", IOController.Flag.debug);
            AsyncResult aResult = (AsyncResult)ar;
            ReadConfigFileDelegate readConfigFileDelegate = (ReadConfigFileDelegate)aResult.AsyncDelegate;
            ArrayList filecontent = readConfigFileDelegate.EndInvoke(ar);
            IOController.Log(this, "processDefaultConfigFile filecontent: " + String.Join(",", filecontent), Flag.debug);
            newEtcDefaultConfig = new ArrayList();
            newEtcDefaultConfig.Add("#configfile /etc/default/isc-dhcp-server modified by GM4D");
            foreach (string line in filecontent)
            {
                string trimmedline = line.Trim();
                if (trimmedline.StartsWith("#INTERFACES"))
                {
                    trimmedline = "INTERFACES=\"" + this.settings.OverviewSelectedInterfaceName + "\"";
                }
                else if (trimmedline.StartsWith("INTERFACES"))
                {
                    trimmedline = "INTERFACES=\"" + this.settings.OverviewSelectedInterfaceName + "\"";
                }
                newEtcDefaultConfig.Add(trimmedline);
            }
            IOController.Log(this, "newEtcDefaultConfig created:\n" + string.Join("\n",newEtcDefaultConfig), Flag.debug);
        }
        /// <summary>
        /// saves new config to /etc/default/isc-dhcp-server
        /// </summary>
        public void SaveEtcDefaultConfigFile()
        {
            if (newEtcDefaultConfig != null)
            {
                string filename = Environment.CurrentDirectory.ToString() + "/gm4d-isc-dhcp-server";
                SaveSettingsToFileDelegate saveSettingsToFileDelegate = null;
                saveSettingsToFileDelegate = new SaveSettingsToFileDelegate(writeEtcDefaultConfigFile);
                IAsyncResult saveSettingsToFileResult = saveSettingsToFileDelegate.BeginInvoke(filename, writeEtcDefaultConfigFileComplete, null);
            }
        }
        /// <summary>
        /// writes newEtcDefaultConfig to file
        /// </summary>
        /// <param name="filename"></param>
        private void writeEtcDefaultConfigFile(String filename)
        {
            System.IO.StreamWriter streamWriter = new System.IO.StreamWriter(filename);
            foreach (string line in this.newEtcDefaultConfig){
                streamWriter.WriteLine(line);
            }
            streamWriter.Flush();
            streamWriter.Close();
        }
        /// <summary>
        /// makes backup of /etc/default/isc-dhcp-server and writes new /etc/default/isc-dhcp-server file
        /// </summary>
        /// <param name="ar"></param>
        private void writeEtcDefaultConfigFileComplete(IAsyncResult ar)
        {
            if (OsIsUnix)
            {
                shellStartInfo.Arguments = "-c \"gksudo cp /etc/default/isc-dhcp-server /etc/default/isc-dhcp-server.bak;gksudo mv " + Environment.CurrentDirectory.ToString() + "/gm4d-isc-dhcp-server /etc/default/isc-dhcp-server\"";
                shellProc.Start();
                IOController.Log(this, "new /etc/default/isc-dhcp-server applied " + shellProc.StandardOutput.ReadToEnd(), Flag.debug);
                shellProc.WaitForExit();
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// retrieves information about the host computer such as network interfaces, IP addresses etc. and stores the information in settings
        /// </summary>
        public void GetHostInfo()
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
            NetCalcTool nct = settings.HostNetCalcTool;
            // loop through all network interfaces
            foreach (NetworkInterface adapter in nics)
            {
                //check if interface is loopback
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    //skip if loopback
                    continue;
                }
                //check if adapter is up
                if (adapter.OperationalStatus != OperationalStatus.Up)
                {
                    //if not up skip this interface
                    continue;
                }
                //create new HostNIC
                HostNIC nic = new HostNIC();
                //get name, id, hardware address, interface type
                nic.Name = adapter.Name;
                nic.Id = adapter.Id;
                nic.MacAddress = adapter.GetPhysicalAddress().ToString();
                nic.Type = adapter.NetworkInterfaceType.ToString();
                //get ipv4 status
                nic.Ipv4Enabled = adapter.Supports(NetworkInterfaceComponent.IPv4);
                if (adapter.Supports(NetworkInterfaceComponent.IPv4))
                {
                    nic.StaticIPAddress = !adapter.GetIPProperties().GetIPv4Properties().IsDhcpEnabled;
                    // get gateway addresses and set first if present
                    GatewayIPAddressInformationCollection gateways = adapter.GetIPProperties().GatewayAddresses;
                    if (gateways.Count > 0)
                    {
                        nic.Gateway = gateways[0].Address.ToString();
                    }
                    // get unicast addresses
                    foreach (UnicastIPAddressInformation unicastIPAddressInformation in adapter.GetIPProperties().UnicastAddresses)
                    {
                        // check if ipv4 address
                        if (unicastIPAddressInformation.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            // set ip adress
                            nic.IPAddress = unicastIPAddressInformation.Address.ToString();
                            // get subnet mask
                            String snetMask;
                            try
                            {
                                // IPv4Mask not implemented in mono at current state
                                snetMask = unicastIPAddressInformation.IPv4Mask.ToString();
                            }
                            catch (NotImplementedException e)
                            {
                                IOController.Log(this, "failed to get subnetmask with unicastIPAddressInformation.IPv4Mask, using workaround", Flag.status);
                                // workaround to get subnetmask in unix environment with mono using the custom SubnetMask class 
                                snetMask = "";
                                snetMask = SubnetMask.GetIPv4Mask(adapter.Name);
                            }
                            nic.SubnetMask = snetMask;
                        }
                    }
                    //try to get the dns addresses of adapter
                    try
                    {
                        IPAddressCollection dnsAddresses = adapter.GetIPProperties().DnsAddresses;
                        if (dnsAddresses.Count >= 1) nic.PrimaryDNS = dnsAddresses[0].ToString();
                        if (dnsAddresses.Count >= 2) nic.SecondaryDNS = dnsAddresses[0].ToString();
                    }
                    catch (Exception e)
                    {
                        IOController.Log(this, "failed to get DNS server addresses " + e, Flag.error);
                    }
                    //calculate network id 
                    try
                    {
                        nct.calculate(nic.IPAddress, nic.SubnetMask);
                        nic.SubnetIdentifier = nct.NetworkId;
                    }
                    catch (Exception e)
                    {
                        IOController.Log(this, "failed calculate network address " + e, Flag.error);
                    }
                }
                //add NIC to settings
                settings.AddInterface(nic);
            }
        }
        /// <summary>
        /// gets the installation status of the dhcp server 
        /// </summary>
        public void GetDHCPServerInstallStatus()
        {
            this.settings.OverviewDhcpServerInstallStatus = "no status";
            if (OsIsUnix)
            {
                shellStartInfo.Arguments = "-c \" dpkg-query -s isc-dhcp-server | head -n2 | tail -n1 | cut -f3 -d' '\"";
                shellProc.Start();
                string strOutput = shellProc.StandardOutput.ReadToEnd();
                strOutput = strOutput.Trim();
                IOController.Log(this, "GetDHCPServerInstallStatus " + strOutput, Flag.status);
                if (strOutput.Contains("ok"))
                {
                    this.settings.OverviewDhcpServerInstallStatus = "installed";
                    this.settings.IsDHCPServerInstalled = true;
                }
                else
                {
                    this.settings.OverviewDhcpServerInstallStatus = "not installed";
                    this.settings.IsDHCPServerInstalled = false;
                }
                shellProc.WaitForExit();
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// gets the installation status of the gksu package
        /// </summary>
        /// <returns></returns>
        public bool GetGksudoInstallStatus()
        {
            this.settings.OverviewDhcpServerInstallStatus = "no status";
            if (OsIsUnix)
            {
                shellStartInfo.Arguments = "-c \" dpkg-query -s gksu | head -n2 | tail -n1 | cut -f3 -d' '\"";
                shellProc.Start();
                string strOutput = shellProc.StandardOutput.ReadToEnd();
                strOutput = strOutput.Trim();
                IOController.Log(this, "GetGksudoInstallStatus " + strOutput, Flag.status);
                if (strOutput.Contains("ok"))
                {
                    return true;
                }
                else
                {
                    return false;
                }
                shellProc.WaitForExit();
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
            return false;
        }
        /// <summary>
        /// get the running status of the dhcpd service
        /// </summary>
        public void GetDHCPServerStatus()
        {
            if (OsIsUnix)
            {
                SaveSettingsFile(Environment.CurrentDirectory.ToString() + "/dhcpd.gm4d");
                shellStartInfo.Arguments = string.Format("-c \"gksudo service isc-dhcp-server status\"");
                shellProc.Start();
                string strOutput = shellProc.StandardOutput.ReadToEnd();
                shellProc.WaitForExit();
                IOController.Log(this, "GetDHCPServerStatus " + strOutput, Flag.status);
                if (strOutput.Contains("start"))
                {
                    this.settings.IsDHCPServerRunning = true;
                    this.settings.OverviewDhcpServerStatus = "running";
                }
                else if (strOutput.Contains("stop"))
                {
                    this.settings.IsDHCPServerRunning = false;
                    this.settings.OverviewDhcpServerStatus = "stopped";
                }
                else
                {
                    this.settings.IsDHCPServerRunning = false;
                    this.settings.OverviewDhcpServerStatus = "no status";
                    throw new System.Exception("unknown status " + strOutput);
                }
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// starts the dhcpd service
        /// </summary>
        public void StartDHCPServer()
        {
            if (OsIsUnix)
            {
                SaveSettingsFile(Environment.CurrentDirectory.ToString() + "/dhcpd.gm4d");
                shellStartInfo.Arguments = string.Format("-c \"gksudo service isc-dhcp-server start\"");
                shellProc.Start();
                IOController.Log(this, "StartDHCPServer " + shellProc.StandardOutput.ReadToEnd(), Flag.debug);
                shellProc.WaitForExit();
                GetDHCPServerStatus();
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// stops the dhcpd service
        /// </summary>
        public void StopDHCPServer()
        {
            if (OsIsUnix)
            {
                SaveSettingsFile(Environment.CurrentDirectory.ToString() + "/dhcpd.gm4d");
                shellStartInfo.Arguments = string.Format("-c \"gksudo service isc-dhcp-server stop\"");
                shellProc.Start();
                IOController.Log(this, "StopDHCPServer " + shellProc.StandardOutput.ReadToEnd(), Flag.debug);
                shellProc.WaitForExit();
                GetDHCPServerStatus();
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// restarts the dhcpd service
        /// </summary>
        public void RestartDHCPServer()
        {
            if (OsIsUnix && this.settings.IsDHCPServerRunning)
            {
                SaveSettingsFile(Environment.CurrentDirectory.ToString() + "/dhcpd.gm4d");
                shellStartInfo.Arguments = string.Format("-c \"gksudo service isc-dhcp-server restart\"");
                shellProc.Start();
                IOController.Log(this, "RestartDHCPServer " + shellProc.StandardOutput.ReadToEnd(), Flag.debug);
                shellProc.WaitForExit();
                GetDHCPServerStatus();
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// FileSystemWatcher to  track changes in a file
        /// </summary>
        public FileSystemWatcher DhcpdLeasesFileWatcher { get; set; }
        /// <summary>
        /// initialises the file watcher to track change sin the dhcpd.leases file
        /// </summary>
        public void InitiateDhcpdLeasesFileWatcher()
        {
            if (OsIsUnix)
            {
                if (!File.Exists("/var/lib/dhcp/dhcpd.leases"))
                {
                    shellStartInfo.Arguments = string.Format("-c \"gksudo touch /var/lib/dhcp/dhcpd.leases\"");
                    IOController.Log(this, "initiateDhcpdLeasesFileWatcher create dhcpd.leases " + shellProc.StandardOutput.ReadToEnd(), Flag.debug);
                    shellProc.WaitForExit();
                }
                if (File.Exists("/var/lib/dhcp/dhcpd.leases"))
                {
                    IOController.Log(this, "initiateDhcpdLeasesFileWatcher file /var/lib/dhcp/dhcpd.leases is present", Flag.debug);
                    // create a new FileSystemWatcher
                    this.DhcpdLeasesFileWatcher = new FileSystemWatcher();
                    string path = Path.Combine("/", "var", "lib", "dhcp");
                    // set path
                    this.DhcpdLeasesFileWatcher.Path = path;
                    // watch for changes in LastAccess and LastWrite times
                    this.DhcpdLeasesFileWatcher.NotifyFilter = NotifyFilters.LastWrite;
                    // only watch a specific file
                    this.DhcpdLeasesFileWatcher.Filter = "dhcpd.leases";
                    // add event handler
                    this.DhcpdLeasesFileWatcher.Changed += new FileSystemEventHandler(OnDhcpdLeasesChanged);
                    // start watching.
                    this.DhcpdLeasesFileWatcher.EnableRaisingEvents = true;
                }
                else
                {
                    throw new FileNotFoundException("/var/lib/dhcp/dhcpd.leases not found");
                }
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// is called by the file watcher is the dhcpd.leases file changes
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void OnDhcpdLeasesChanged(object source, FileSystemEventArgs e)
        {
            IOController.Log(this, "OnDhcpdLeasesChanged: " + e.FullPath.ToString(), Flag.debug);
            this.ReadDhcpdLeasesFile(e.FullPath);
        }
        /// <summary>
        /// reads the content of the dhcpd.leases file
        /// </summary>
        /// <param name="obj"></param>
        public void ReadDhcpdLeasesFile(object obj)
        {
            IOController.Log(this, "ReadDhcpdLeasesFile start", Flag.debug);
            string filename = obj.ToString();
            if (OsIsUnix)
            {
                if (File.Exists(filename))
                {
                    IOController.Log(this, "ReadDhcpdLeasesFile filename: " + filename, Flag.debug);
                    ReadDhcpdLeasesFileDelegate readDhcpdLeasesFileDelegate = new ReadDhcpdLeasesFileDelegate(ProcessDhcpdLeasesFile);
                    IAsyncResult readDhcpdLeasesFileResult = readDhcpdLeasesFileDelegate.BeginInvoke(filename, parseDhcpdLeasesFile, null);
                }
                else
                {
                    throw new FileNotFoundException(filename + " not found");
                }
            }
            else
            {
                throw new System.Exception("System is not a Unix environment");
            }
        }
        /// <summary>
        /// processes the content of the dhcpd.leases file
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        private ArrayList ProcessDhcpdLeasesFile(string filename)
        {
            IOController.Log(this, "ProcessDhcpdLeasesFile filename: " + filename, Flag.debug);
            if (!File.Exists(filename))
            {
                IOController.Log(this, filename + " does not exist", Flag.error);
                return null;
            }
            using (StreamReader sr = File.OpenText(filename))
            {
                string input;
                ArrayList filecontent = new ArrayList();
                while ((input = sr.ReadLine()) != null)
                {
                    filecontent.Add(input);
                }
                return filecontent;
            }
        }
        /// <summary>
        /// parses the leases from the filecontent of the dhcpd.leasses file
        /// </summary>
        /// <param name="result"></param>
        private void parseDhcpdLeasesFile(IAsyncResult result)
        {
            IOController.Log(this, "parseDhcpdLeasesFile start", Flag.debug);
            System.Collections.Generic.Dictionary<String, DhcpdLease> dhcpdLeasesList = new System.Collections.Generic.Dictionary<String, DhcpdLease>();
            DhcpdLease dhcpdLease = new DhcpdLease();
            try
            {
                AsyncResult aResult = (AsyncResult)result;
                ReadDhcpdLeasesFileDelegate readDhcpdLeasesFileDelegate = (ReadDhcpdLeasesFileDelegate)aResult.AsyncDelegate;
                ArrayList filecontent = readDhcpdLeasesFileDelegate.EndInvoke(result);
                this.settings.DhcpdLeases.Clear();
                //IOController.Log(this, "parseDhcpdLeasesFile finished EndInvoke", Flag.debug);
                foreach (string line in filecontent)
                {
                    string cleanline = Regex.Replace(line, @"\s+", " ");
                    cleanline = cleanline.Trim();
                    if (cleanline.StartsWith("lease"))
                    {
                        dhcpdLease = new DhcpdLease();
                        int endindex = cleanline.IndexOf("{") - 1;
                        int startindex = 6;
                        dhcpdLease.IPAddress = cleanline.Substring(startindex, endindex - startindex);
                    }
                    else if (cleanline.StartsWith("hardware ethernet"))
                    {
                        int endindex = cleanline.IndexOf(";");
                        int startindex = cleanline.IndexOf("hardware ethernet") + 18;
                        dhcpdLease.MACAddress = cleanline.Substring(startindex, endindex - startindex);
                    }
                    else if (cleanline.StartsWith("client-hostname"))
                    {
                        int endindex = cleanline.IndexOf(";") - 1;
                        int startindex = cleanline.IndexOf("client-hostname") + 17;
                        dhcpdLease.DeviceName = cleanline.Substring(startindex, endindex - startindex);
                    }
                    else if (cleanline.StartsWith("starts"))
                    {
                        string[] strArr = cleanline.Split(' ');
                        if (strArr.Length > 3)
                        {
                            strArr[3] = strArr[3].TrimEnd(';');
                            dhcpdLease.LeaseStart = strArr[2] + " " + strArr[3];
                        }
                    }
                    else if (cleanline.StartsWith("ends"))
                    {
                        string[] strArr = cleanline.Split(' ');
                        if (strArr.Length > 3)
                        {
                            strArr[3] = strArr[3].TrimEnd(';');
                            dhcpdLease.LeaseEnd = strArr[2] + " " + strArr[3];
                        }
                    }
                    else if (cleanline.StartsWith("binding state"))
                    {
                        string[] strArr = cleanline.Split(' ');
                        if (strArr.Length > 2)
                        {
                            strArr[2] = strArr[2].TrimEnd(';');
                            dhcpdLease.LeaseState = strArr[2];
                        }
                    }
                    else if (cleanline.Contains("}"))
                    {
                        dhcpdLeasesList[dhcpdLease.MACAddress] = dhcpdLease;
                        //IOController.Log(this, "found dhcpdLease: " + dhcpdLease.ToString(), Flag.debug);
                    }
                }
            }
            catch (Exception e)
            {
                IOController.Log(this, "parsing failed " + e.ToString());
            }
            this.settings.DhcpdLeases = dhcpdLeasesList;
            IOController.Log(this, "Active Leases found:\n" + string.Join("\n", dhcpdLeasesList.Select(x => x.Key + "=" + x.Value).ToArray()), Flag.status);
        }
        /// <summary>
        /// flag for the type of the log (error, status or debug)
        /// </summary>
        public enum Flag { debug, error, status}
        /// <summary>
        /// creates a log entry
        /// </summary>
        /// <param name="sender">sender of the log (use "this")</param>
        /// <param name="message">message or data to log</param>
        /// <param name="flag">IOController.Flag parameter</param>
        public static void Log(object sender, string message, Flag flag = Flag.error)
        {
            string strFlag;
            switch (flag)
            {
                case Flag.error:
                    System.Console.BackgroundColor = ConsoleColor.Red;
                    strFlag = "[ERROR]";
                    break;
                case Flag.debug:
                    System.Console.ForegroundColor = ConsoleColor.Green;
                    strFlag = "[DEBUG]";
                    break;
                case Flag.status:
                    strFlag = "[STATUS]";
                    break;
                default: 
                    strFlag = ""; 
                    break;
            }
            System.Diagnostics.StackTrace s = new System.Diagnostics.StackTrace(System.Threading.Thread.CurrentThread, true);
            string className = sender.GetType().FullName;
            string methodName = s.GetFrame(1).GetMethod().Name;
            string fileName = s.GetFrame(1).GetFileName();
            int lineNumber = s.GetFrame(1).GetFileLineNumber();
            string logheader = System.DateTime.Now + " " + strFlag + " " + className + "." + methodName + ":";
            string filename = Path.Combine(Environment.CurrentDirectory.ToString(),"gm4d.log");
            if (!File.Exists(filename))
            {
                using (StreamWriter sw = File.AppendText(filename))
                {
                    sw.WriteLine("###############################################");
                    sw.WriteLine("#               GM4D Log file                 #");
                    sw.WriteLine("###############################################");
                }
            }
            using (StreamWriter sw = File.AppendText(filename))
            {
                sw.WriteLine(logheader);
                sw.WriteLine(message);
                sw.WriteLine();
                sw.Flush();
                sw.Close();
            }   
        }
    }
}