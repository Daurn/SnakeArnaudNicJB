using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;

namespace SnakeArnaudNicJB.Tools;

public class AzureTools
{
    private const string TENANT_ID = "b7b023b8-7c32-4c02-92a6-c8cdaa1d189c";
    private const string SUBSCRIPTION_ID = "ca5f9d2f-dd34-435f-8724-850f99c6d2e5";
    private const string CLIENT_ID = "a8cbbe48-281e-4942-8963-31df6cda6971";
    private const string CLIENT_SECRET = "TQs8Q~xQ.icv1-7ljmAuQ7TXm-GQfRYDNgMsxc7y";
    private readonly string resourceName;

    private readonly SubscriptionResource subscription;


    public AzureTools(string resourceName)
    {
        ArmClient client = new(new ClientSecretCredential(TENANT_ID, CLIENT_ID, CLIENT_SECRET));
        subscription = client.GetSubscriptions().Get(SUBSCRIPTION_ID);
        this.resourceName = resourceName;
    }

    public async Task<ResourceGroupResource> GetResourceGroupAsync()
    {
        var resourceGroups = subscription.GetResourceGroups();

        // With the collection, we can create a new resource group with an specific name
        var resourceGroupName = $"rg-{resourceName}";

        // Check if resource exist or not
        bool exists = await resourceGroups.ExistsAsync(resourceGroupName);

        // If not exists
        if (!exists)
        {
            // Set location
            var location = AzureLocation.FranceCentral;
            var resourceGroupData = new ResourceGroupData(location);

            // Create Resource group
            await resourceGroups.CreateOrUpdateAsync(WaitUntil.Completed, resourceGroupName, resourceGroupData);
        }

        // Return new resource group
        return await resourceGroups.GetAsync(resourceGroupName);
    }

    private PublicIPAddressResource CreatePublicIp(ResourceGroupResource resourceGroup)
    {
        var ipName = $"ip-{resourceName}";
        var publicIps = resourceGroup.GetPublicIPAddresses();

        //Create public ip
        return publicIps.CreateOrUpdate(
            WaitUntil.Completed,
            ipName,
            new PublicIPAddressData
            {
                PublicIPAddressVersion = NetworkIPVersion.IPv4,
                PublicIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                Location = AzureLocation.FranceCentral
            }).Value;
    }

    private VirtualNetworkResource CreateVnet(ResourceGroupResource resourceGroup)
    {
        var vnetName = $"vnet-{resourceName}";
        var vnc = resourceGroup.GetVirtualNetworks();

        // Create vnetResource
        return vnc.CreateOrUpdate(
            WaitUntil.Completed,
            vnetName,
            new VirtualNetworkData
            {
                Location = AzureLocation.FranceCentral,
                Subnets =
                {
                    new SubnetData
                    {
                        Name = "VMsubnet",
                        AddressPrefix = "10.0.0.0/24"
                    }
                },
                AddressPrefixes =
                {
                    "10.0.0.0/16"
                }
            }).Value;
    }

    private NetworkInterfaceResource CreateNetworkInterface(VirtualNetworkResource vnet,
        PublicIPAddressResource ipAddress, ResourceGroupResource resourceGroup)
    {
        var nicName = $"nic-{resourceName}";
        var nics = resourceGroup.GetNetworkInterfaces();

        //Create network interface
        return nics.CreateOrUpdate(
            WaitUntil.Completed,
            nicName,
            new NetworkInterfaceData
            {
                Location = AzureLocation.FranceCentral,
                IPConfigurations =
                {
                    new NetworkInterfaceIPConfigurationData
                    {
                        Name = "Primary",
                        Primary = true,
                        Subnet = new SubnetData { Id = vnet?.Data.Subnets.First().Id },
                        PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                        PublicIPAddress = new PublicIPAddressData { Id = ipAddress?.Data.Id }
                    }
                }
            }).Value;
    }

    public VirtualMachineResource CreateVirtualMachine(ResourceGroupResource resourceGroup, string adminUsername,
        string adminPassword)
    {
        var ip = CreatePublicIp(resourceGroup);
        var vnet = CreateVnet(resourceGroup);
        var interfaceNetwork = CreateNetworkInterface(vnet, ip, resourceGroup);

        var vms = resourceGroup.GetVirtualMachines();

        //Virtual machine
        return vms.CreateOrUpdate(
            WaitUntil.Completed,
            $"vm-{resourceName}",
            new VirtualMachineData(AzureLocation.FranceCentral)
            {
                HardwareProfile = new VirtualMachineHardwareProfile
                {
                    VmSize = VirtualMachineSizeType.StandardB1S
                },
                OSProfile = new VirtualMachineOSProfile
                {
                    ComputerName = $"vm-{resourceName}",
                    AdminUsername = adminUsername,
                    AdminPassword = adminPassword,
                    LinuxConfiguration = new LinuxConfiguration
                    {
                        DisablePasswordAuthentication = false,
                        ProvisionVmAgent = true
                    }
                },
                StorageProfile = new VirtualMachineStorageProfile
                {
                    OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage),
                    ImageReference = new ImageReference
                    {
                        Offer = "UbuntuServer",
                        Publisher = "Canonical",
                        Sku = "18.04-LTS",
                        Version = "latest"
                    }
                },
                NetworkProfile = new VirtualMachineNetworkProfile
                {
                    NetworkInterfaces =
                    {
                        new VirtualMachineNetworkInterfaceReference
                        {
                            Id = interfaceNetwork.Id
                        }
                    }
                }
            }).Value;
    }

    public async void VmPowerOnAsync()
    {
        var resourceGroup = await GetResourceGroupAsync();

        var vms = resourceGroup.GetVirtualMachines();
        await vms.First().PowerOnAsync(WaitUntil.Completed);
    }

    public async void VmPowerOffsync()
    {
        var resourceGroup = await GetResourceGroupAsync();

        var vms = resourceGroup.GetVirtualMachines();
        await vms.First().PowerOffAsync(WaitUntil.Completed);
    }

    public async Task<string> GetIpAdress(string ipName)
    {
        var resourceGroup = await GetResourceGroupAsync();

        var publicIPAddresses = resourceGroup.GetPublicIPAddresses();
        PublicIPAddressResource publicIPAddress = publicIPAddresses.Get(ipName);

        return publicIPAddress.Data.IPAddress;
    }
}