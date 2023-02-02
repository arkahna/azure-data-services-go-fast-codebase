# Generated by Terragrunt. Sig: nIlQXj57tbuaRZEa
    data "terraform_remote_state" "layer2" {
      # The settings here should match the "backend" settings in the
      # configuration that manages the network resources.
      backend = "azurerm"
      
      config = {
        container_name       = "tstate"
        key                  = "terraform_layer2.tfstate"
        resource_group_name  = "TestGoFast"
        storage_account_name = "testgofaststate"
      }
    }
