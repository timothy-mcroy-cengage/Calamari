﻿## Octopus Azure Service Fabric Context script, version 1.0
## --------------------------------------------------------------------------------------
## 
## This script is used to establish a connection to the Azure Service Fabric cluster
##
## The script is passed the following parameters. 
##
##   OctopusAzureTargetScript
##   OctopusAzureTargetScriptParameters
##   OctopusFabricConnectionEndpoint                         // The connection endpoint
##   OctopusFabricIsSecure                                   // Indicates whether the fabric connection is secured by an X509 cert
##   OctopusFabricServerCertificateThumbprint                // The server cert thumbprint
##   OctopusFabricClientCertificateThumbprint                // The client cert thumbprint

$ErrorActionPreference = "Stop"

function Execute-WithRetry([ScriptBlock] $command) {
    $attemptCount = 0
    $operationIncomplete = $true
    $sleepBetweenFailures = 5
    $maxFailures = 5

    while ($operationIncomplete -and $attemptCount -lt $maxFailures) {
        $attemptCount = ($attemptCount + 1)

        if ($attemptCount -ge 2) {
            Write-Host "Waiting for $sleepBetweenFailures seconds before retrying..."
            Start-Sleep -s $sleepBetweenFailures
            Write-Host "Retrying..."
        }

        try {
            & $command

            $operationIncomplete = $false
        } catch [System.Exception] {
            if ($attemptCount -lt ($maxFailures)) {
                Write-Host ("Attempt $attemptCount of $maxFailures failed: " + $_.Exception.Message)
            } else {
                throw
            }
        }
    }
}

Execute-WithRetry{

	$ClusterConnectionParameters = @()
	$ClusterConnectionParameters["ConnectionEndpoint"] = $OctopusFabricConnectionEndpoint

    If ([System.Convert]::ToBoolean($OctopusFabricIsSecure)) {
        # Secure (client certificate)
        Write-Verbose "Connect to Service Fabric securely (client certificate)"
		$ClusterConnectionParameters["ServerCertificateThumbprint"] = $OctopusFabricServerCertificateThumbprint
		$ClusterConnectionParameters["X509Credential"] = $true
		$ClusterConnectionParameters["StoreLocation"] = "LocalMachine"
		$ClusterConnectionParameters["StoreName"] = "MY"
		$ClusterConnectionParameters["FindType"] = "FindByThumbprint"
		$ClusterConnectionParameters["FindValue"] = $OctopusFabricClientCertificateThumbprint
    } Else {
        # Unsecure
        Write-Verbose "Connect to Service Fabric unsecurely"
    }

    try
    {
        Write-Verbose "Authenticating with Service Fabric"
        [void](Connect-ServiceFabricCluster @ClusterConnectionParameters)

		# http://stackoverflow.com/questions/35711540/how-do-i-deploy-service-fabric-application-from-vsts-release-pipeline
		# When the Connect-ServiceFabricCluster function is called, a local $clusterConnection variable is set after the call to Connect-ServiceFabricCluster. You can see that using Get-Variable.
		# Unfortunately there is logic in some of the SDK scripts that expect that variable to be set but because they run in a different scope, that local variable isn't available.
		# It works in Visual Studio because the Deploy-FabricApplication.ps1 script is called using dot source notation, which puts the $clusterConnection variable in the current scope.
		# I'm not sure if there is a way to use dot sourcing when running a script though the release pipeline but you could, as a workaround, make the $clusterConnection variable global right after it's been set via the Connect-ServiceFabricCluster call.
		$global:clusterConnection = $clusterConnection
    }
    catch [System.Fabric.FabricObjectClosedException]
    {
        Write-Warning "Service Fabric cluster may not be connected."
        throw
    }
}

Write-Verbose "Invoking target script $OctopusAzureTargetScript with $OctopusAzureTargetScriptParameters parameters"

try {
    Invoke-Expression ". $OctopusAzureTargetScript $OctopusAzureTargetScriptParameters"
} catch {
    # Warn if FIPS 140 compliance required when using Service Management SDK
    if ([System.Security.Cryptography.CryptoConfig]::AllowOnlyFipsAlgorithms -and ![System.Convert]::ToBoolean($OctopusUseServicePrincipal)) {
        Write-Warning "The Azure Service Management SDK is not FIPS 140 compliant. http://g.octopushq.com/FIPS"
    }
    
    throw
}