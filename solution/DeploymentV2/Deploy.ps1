#----------------------------------------------------------------------------------------------------------------
# You must be logged into the Azure CLI to run this script
#----------------------------------------------------------------------------------------------------------------
# This script will:
# - Deploy all infra resources using terra
# - Approve all private link requests
# - Build and deploy web app
# - Build and deploy function app
# - Build database app and deploy
# - Deploy samples into blob storage
# 
# This is intended for creating a once off deployment from your development machine. You should setup the
# GitHub actions for your long term prod/non-prod environments
#
# Intructions
# - Ensure that you have run the Prepare.ps1 script first. This will prepare your azure subscription for deployment
# - Ensure that you have run az login and az account set
# - Ensure you have Owner access to the resource group you are planning on deploying to
# - Run this script
# 
# You can run this script multiple times if needed.
# 
#----------------------------------------------------------------------------------------------------------------


#Cool Branding :-)
figlet Azure Data Services -t | lolcat &&  figlet Go Fast -t | lolcat

import-Module ./pwshmodules/Deploy_0_Prep.psm1 -force

$PathToReturnTo = (Get-Location).Path
$deploymentFolderPath = (Get-Location).Path 
$gitDeploy = ([System.Environment]::GetEnvironmentVariable('gitDeploy')  -eq 'true')

PrepareDeployment -gitDeploy $gitDeploy -deploymentFolderPath $deploymentFolderPath -FeatureTemplate "" -PathToReturnTo $PathToReturnTo

Set-Location $deploymentFolderPath 
Set-Location ./terraform_layer0
terragrunt init -reconfigure --terragrunt-config vars/$env:environmentName/terragrunt.hcl
if(($env:TF_VAR_remove_lock -eq $true) -and ($env:TF_VAR_lock_id -ne "#####"))
{
    terraform force-unlock $env:TF_VAR_lock_id -force
}

Set-Location $deploymentFolderPath 
Set-Location ./terraform_layer0
terragrunt init -reconfigure --terragrunt-config vars/$env:environmentName/terragrunt.hcl
./00-deploy.ps1

Set-Location $deploymentFolderPath 
Set-Location ./terraform_layer1
terragrunt init -reconfigure --terragrunt-config vars/$env:environmentName/terragrunt.hcl
./01-deploy.ps1

Set-Location $deploymentFolderPath
Set-Location ./terraform_layer2
terragrunt init -reconfigure --terragrunt-config vars/$env:environmentName/terragrunt.hcl
./02-deploy.ps1

Set-Location $deploymentFolderPath
Set-Location ./terraform_layer3
terragrunt init -reconfigure --terragrunt-config vars/$env:environmentName/terragrunt.hcl
./03-deploy.ps1
./03-publish.ps1

Set-Location $deploymentFolderPath
Set-Location ./terraform_layer2
./02-publish.ps1

Set-Location $deploymentFolderPath
Write-Host "Finished"
