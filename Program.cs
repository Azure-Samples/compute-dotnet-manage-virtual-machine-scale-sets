// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Samples.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;

namespace ManageVirtualMachineScaleSet
{
    public class Program
    {
        /**
         * Azure Compute sample for managing virtual machine scale sets with un-managed disks -
         *  - Create a virtual machine scale set behind an Internet facing load balancer
         *  - Install Apache Web serversinvirtual machinesinthe virtual machine scale set
         *  - List the network interfaces associated with the virtual machine scale set
         *  - List scale set virtual machine instances and SSH collection string
         *  - Stop a virtual machine scale set
         *  - Start a virtual machine scale set
         *  - Update a virtual machine scale set
         *    - Double the no. of virtual machines
         *  - Restart a virtual machine scale set
         */

        public static void RunSample(ArmClient client)
        {
            AzureLocation region = AzureLocation.EastUS;
            var rgName = Utilities.CreateRandomName("rgCOVS");
            var vmssNetworkConfigurationName = Utilities.CreateRandomName("networkConfiguration");
            var ipConfigurationName = Utilities.CreateRandomName("ipconfigruation");
            var vnetName = Utilities.CreateRandomName("vnet");
            var loadBalancerName1 = Utilities.CreateRandomName("intlb" + "-");
            var publicIpName = "pip-" + loadBalancerName1;
            var frontendName = loadBalancerName1 + "-FE1";
            var backendPoolName1 = loadBalancerName1 + "-BAP1";
            var backendPoolName2 = loadBalancerName1 + "-BAP2";
            var domainNameLabel = Utilities.CreateRandomName("domain");

            var httpProbe = "httpProbe";
            var httpsProbe = "httpsProbe";
            var httpLoadBalancingRule = "httpRule";
            var httpsLoadBalancingRule = "httpsRule";
            var natPool50XXto22 = "natPool50XXto22";
            var natPool60XXto23 = "natPool60XXto23";
            var vmssName = Utilities.CreateRandomName("vmss");

            var userName = Utilities.CreateUsername();
            var sshKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQCfSPC2K7LZcFKEO+/t3dzmQYtrJFZNxOsbVgOVKietqHyvmYGHEC0J2wPdAqQ/63g/hhAEFRoyehM+rbeDri4txB3YFfnOK58jqdkyXzupWqXzOrlKY4Wz9SKjjN765+dqUITjKRIaAip1Ri137szRg71WnrmdP3SphTRlCx1Bk2nXqWPsclbRDCiZeF8QOTi4JqbmJyK5+0UqhqYRduun8ylAwKKQJ1NJt85sYIHn9f1Rfr6Tq2zS0wZ7DHbZL+zB5rSlAr8QyUdg/GQD+cmSs6LvPJKL78d6hMGk84ARtFo4A79ovwX/Fj01znDQkU6nJildfkaolH2rWFG/qttD azjava@javalib.Com";

            var apacheInstallScript = "https://raw.githubusercontent.com/Azure/azure-libraries-for-net/master/Samples/Asset/install_apache.sh";
            var fileUris = new List<string>();
            var lro = client.GetDefaultSubscription().GetResourceGroups().CreateOrUpdate(Azure.WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
            var resourceGroup = lro.Value;
            fileUris.Add(apacheInstallScript);
            try
            {
                //=============================================================
                // Create a virtual network with a frontend subnet
                Utilities.Log("Creating virtual network with a frontend subnet ...");

                var networkCollection = resourceGroup.GetVirtualNetworks();
                var networkData = new VirtualNetworkData()
                {
                    Location = region,
                    AddressPrefixes =
                    {
                        "172.16.1.0/24"
                    },
                    Subnets =
                    {
                        new SubnetData()
                        {
                            Name = "Front-end",
                        }
                    },
                };
                var networkResource = networkCollection.CreateOrUpdate(Azure.WaitUntil.Completed, vnetName, networkData).Value;
                Utilities.Log("Created a virtual network");
                // Print the virtual network details
                Utilities.PrintVirtualNetwork(networkResource);

                //=============================================================
                // Create a public IP address
                Utilities.Log("Creating a public IP address...");

                var publicIpAddressCollection = resourceGroup.GetPublicIPAddresses();
                var publicIPAddressData = new PublicIPAddressData()
                {
                    Location = region,
                    Tags = { { "key", "value" } },
                    PublicIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                    DnsSettings = new PublicIPAddressDnsSettings()
                    {
                        DomainNameLabel = domainNameLabel
                    }
                };
                var publicIpAddress = publicIpAddressCollection.CreateOrUpdate(Azure.WaitUntil.Completed, publicIpName, publicIPAddressData).Value;
                Utilities.Log("Created a public IP address");
                // Print the virtual network details
                Utilities.PrintIPAddress(publicIpAddress);

                //=============================================================
                // Create an Internet facing load balancer with
                // One frontend IP address
                // Two backend address pools which contain network interfaces for the virtual
                //  machines to receive HTTP and HTTPS network traffic from the load balancer
                // Two load balancing rules for HTTP and HTTPS to map public ports on the load
                //  balancer to ports in the backend address pool
                // Two probes which contain HTTP and HTTPS health probes used to check availability
                //  of virtual machines in the backend address pool
                // Three inbound NAT rules which contain rules that map a public port on the load
                //  balancer to a port for a specific virtual machineinthe backend address pool
                //  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23

                Utilities.Log("Creating a Internet facing load balancer with ...");
                Utilities.Log("- A frontend IP address");
                Utilities.Log("- Two backend address pools which contain network interfaces for the virtual\n"
                        + "  machines to receive HTTP and HTTPS network traffic from the load balancer");
                Utilities.Log("- Two load balancing rules for HTTP and HTTPS to map public ports on the load\n"
                        + "  balancer to portsinthe backend address pool");
                Utilities.Log("- Two probes which contain HTTP and HTTPS health probes used to check availability\n"
                        + "  of virtual machinesinthe backend address pool");
                Utilities.Log("- Two inbound NAT rules which contain rules that map a public port on the load\n"
                        + "  balancer to a port for a specific virtual machineinthe backend address pool\n"
                        + "  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23");

                var loadBalancerCollection = resourceGroup.GetLoadBalancers();
                var loadBalancerData = new LoadBalancerData()
                {
                    Location = region,
                    LoadBalancingRules =
                    {
                        new LoadBalancingRuleData()
                        {
                            Name = httpLoadBalancingRule,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 80,
                            ProbeId = new ResourceIdentifier(httpProbe),
                            BackendAddressPools =
                            {
                                new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                {
                                    Id = new ResourceIdentifier(backendPoolName1)
                                }
                            }
                        },
                        new LoadBalancingRuleData()
                        {
                            Name= httpsLoadBalancingRule,
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 443,
                            ProbeId = new ResourceIdentifier(httpsProbe),
                            BackendAddressPools =
                            {
                                new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                {
                                    Id = new ResourceIdentifier(backendPoolName2)
                                }
                            }
                        }
                    },
                    // Add nat pools to enable direct VM connectivity for
                    //  SSH to port 22 and TELNET to port 23
                    InboundNatRules =
                    {
                        new InboundNatRuleData()
                        {
                            Name = natPool50XXto22,
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 22,
                            FrontendPortRangeStart = 5000,
                            FrontendPortRangeEnd = 5099,
                        },
                        new InboundNatRuleData()
                        {
                            Name = natPool60XXto23,
                            Protocol= LoadBalancingTransportProtocol.Tcp,
                            BackendPort = 23,
                            FrontendPortRangeStart = 6000,
                            FrontendPortRangeEnd = 6099,
                        }
                    },
                    // Add two probes one per rule
                    Probes =
                    {
                        new ProbeData()
                        {
                            RequestPath = "/",
                            Port = 80,
                        },
                        new ProbeData()
                        {
                            RequestPath = "/",
                            Port = 443,
                        }
                    }
                };
                var loadBalancer = loadBalancerCollection.CreateOrUpdate(Azure.WaitUntil.Completed, loadBalancerName1, loadBalancerData).Value;

                // Print load balancer details
                Utilities.Log("Created a load balancer");
                Utilities.PrintLoadBalancer(loadBalancer);

                //=============================================================
                // Create a virtual machine scale set with three virtual machines
                // And, install Apache Web servers on them

                Utilities.Log("Creating virtual machine scale set with three virtual machines"
                        + "inthe frontend subnet ...");

                var t1 = new DateTime();

                var vmScaleSetVMCollection = resourceGroup.GetVirtualMachineScaleSets();
                var scaleSetData = new VirtualMachineScaleSetData(region)
                {
                    Sku = new ComputeSku()
                    {
                        Name = "StandardD3v2",
                        Capacity = 3,
                        Tier = "Standard"
                    },
                    VirtualMachineProfile = new VirtualMachineScaleSetVmProfile()
                    {
                        OSProfile = new VirtualMachineScaleSetOSProfile()
                        {
                            LinuxConfiguration = new LinuxConfiguration()
                            {
                                SshPublicKeys =
                                {
                                    new SshPublicKeyConfiguration()
                                    {
                                        KeyData = sshKey,
                                        Path = $"/home/{userName}/.ssh/authorized_keys"
                                    }
                                }
                            }
                        },
                        StorageProfile = new VirtualMachineScaleSetStorageProfile()
                        {
                            DataDisks =
                            {
                                new VirtualMachineScaleSetDataDisk(1, DiskCreateOptionType.FromImage)
                                {
                                    DiskSizeGB = 100,
                                    Caching = CachingType.ReadWrite
                                },
                                new VirtualMachineScaleSetDataDisk(2, DiskCreateOptionType.FromImage)
                                {
                                    DiskSizeGB = 100,
                                    Caching = CachingType.ReadWrite
                                },
                                new VirtualMachineScaleSetDataDisk(3, DiskCreateOptionType.FromImage)
                                {
                                    DiskSizeGB = 100,
                                },
                            },
                            ImageReference = new ImageReference()
                            {
                                Publisher = "Canonical",
                                Offer = "UbuntuServer",
                                Sku = "16.04-LTS",
                                Version = "latest"
                            }
                        },
                        NetworkProfile = new VirtualMachineScaleSetNetworkProfile()
                        {
                            NetworkInterfaceConfigurations =
                           {
                               new VirtualMachineScaleSetNetworkConfiguration(vmssNetworkConfigurationName)
                               {
                                   IPConfigurations =
                                   {
                                       new VirtualMachineScaleSetIPConfiguration(ipConfigurationName)
                                       {
                                           LoadBalancerInboundNatPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(natPool50XXto22)
                                               },
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(natPool60XXto23)
                                               }
                                           },
                                           ApplicationGatewayBackendAddressPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(backendPoolName1)
                                               },
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(backendPoolName2)
                                               },
                                           },
                                           LoadBalancerBackendAddressPools =
                                           {
                                               new Azure.ResourceManager.Resources.Models.WritableSubResource()
                                               {
                                                   Id = new ResourceIdentifier(loadBalancer.Data.Name)
                                               }
                                           },
                                           SubnetId = networkResource.Id,
                                           Primary = true,
                                       }
                                   }
                               }
                           }
                        },
                        ExtensionProfile = new VirtualMachineScaleSetExtensionProfile()
                        {
                            Extensions =
                            {
                                new VirtualMachineScaleSetExtensionData()
                                {
                                    Publisher = "Microsoft.OSTCExtensions",
                                    ExtensionType = "CustomScriptForLinux",
                                    TypeHandlerVersion = "1.4",
                                    Settings = new BinaryData("{\"commandToExecute\":\"bash install_apache.sh\"}")
                                }
                            }
                        }
                    },

                };
                var t2 = new DateTime();
                Utilities.Log("Created a virtual machine scale set with "
                        + "3 Linux VMs & Apache Web servers on them: (took "
                        + (t2 - t1).TotalSeconds + " seconds) ");
                Utilities.Log();
                var scaleSet = vmScaleSetVMCollection.CreateOrUpdate(Azure.WaitUntil.Completed, vmssName, scaleSetData).Value;

                // Print virtual machine scale set details
                // Utilities.Print(virtualMachineScaleSet);

                //=============================================================
                // List virtual machine scale set network interfaces

                Utilities.Log("Listing scale set network interfaces ...");
                var vmssNics = scaleSet.Data.VirtualMachineProfile.NetworkProfile.NetworkInterfaceConfigurations;
                foreach (var vmssNic in vmssNics)
                {
                    Utilities.Log(vmssNic.Name);
                }

                //=============================================================
                // List virtual machine scale set instance network interfaces and SSH connection string

                Utilities.Log("Listing scale set virtual machine instance network interfaces and SSH connection string...");
                foreach (var instance in vmScaleSetVMCollection.GetAll())
                {
                    Utilities.Log("Scale set virtual machine instance #" + instance.Id);
                    Utilities.Log(instance.Id);
                    var networkInterfaces = instance.Data.VirtualMachineProfile.NetworkProfile.NetworkInterfaceConfigurations;
                    // Pick the first NIC
                    var networkInterface = networkInterfaces.ElementAt(0);
                    foreach (var ipConfig in networkInterface.IPConfigurations)
                    {
                        if (ipConfig.Primary == true)
                        {
                            var loadbalancer = resourceGroup.GetLoadBalancer(ipConfig.ApplicationGatewayBackendAddressPools.ElementAt(0).Id);
                            var natRules = loadBalancer.Data.InboundNatRules;
                            foreach (var natRule in natRules)
                            {
                                if (natRule.BackendPort == 22)
                                {
                                    Utilities.Log("SSH connection string: " + userName + "@" + publicIpAddress.Data.DnsSettings.ReverseFqdn + ":" + natRule.FrontendPort);
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }

                //=============================================================
                // Stop the virtual machine scale set

                Utilities.Log("Stopping virtual machine scale set ...");
                scaleSet.PowerOff(Azure.WaitUntil.Completed);
                Utilities.Log("Stopped virtual machine scale set");

                //=============================================================
                // Deallocate the virtual machine scale set

                Utilities.Log("De-allocating virtual machine scale set ...");
                scaleSet.Deallocate(WaitUntil.Completed);
                Utilities.Log("De-allocated virtual machine scale set");

                //=============================================================
                // Update the virtual machine scale set by removing and adding disk

                Utilities.Log("Updating virtual machine scale set managed data disks...");
                scaleSet.Update(WaitUntil.Completed, new VirtualMachineScaleSetPatch()
                {
                    VirtualMachineProfile = new VirtualMachineScaleSetUpdateVmProfile()
                    {
                        StorageProfile = new VirtualMachineScaleSetUpdateStorageProfile()
                        {
                            DataDisks =
                            {
                                new VirtualMachineScaleSetDataDisk(0, DiskCreateOptionType.FromImage)
                                {
                                },
                                new VirtualMachineScaleSetDataDisk(200, DiskCreateOptionType.FromImage)
                                {
                                }
                            },
                        }
                    }
                });
                Utilities.Log("Updated virtual machine scale set");

                //=============================================================
                // Start the virtual machine scale set

                Utilities.Log("Starting virtual machine scale set ...");
                scaleSet.PowerOn(WaitUntil.Completed);
                Utilities.Log("Started virtual machine scale set");

                //=============================================================
                // Update the virtual machine scale set
                // - double the no. of virtual machines

                Utilities.Log("Updating virtual machine scale set "
                        + "- double the no. of virtual machines ...");

                scaleSet.Update(WaitUntil.Completed, new VirtualMachineScaleSetPatch()
                {
                    Sku = new ComputeSku()
                    {
                        Capacity = 6,
                    },
                });

                Utilities.Log("Doubled the no. of virtual machinesin"
                        + "the virtual machine scale set");

                //=============================================================
                // re-start virtual machine scale set

                Utilities.Log("re-starting virtual machine scale set ...");
                scaleSet.Restart(Azure.WaitUntil.Completed);
                Utilities.Log("re-started virtual machine scale set");
            }
            finally
            {
                try
                {
                    Utilities.Log("Deleting Resource Group: " + rgName);
                    resourceGroup.Delete(WaitUntil.Completed);
                    Utilities.Log("Deleted Resource Group: " + rgName);
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resourcesinAzure. No clean up is necessary");
                }
                catch (Exception g)
                {
                    Utilities.Log(g);
                }
            }
        }

        public static void Main(string[] args)
        {
            try
            {
                //=============================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                // Print selected subscription
                Utilities.Log("Selected subscription: " + client.GetSubscriptions().Id);

                RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}