/*-----------------------------------------------------------------------

 Copyright (c) Microsoft Corporation.
 Licensed under the MIT license.

-----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FunctionApp.Helpers;
using FunctionApp.Services;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Options;
using FunctionApp.Models.Options;

namespace FunctionApp.Models.GetTaskInstanceJSON
{
    public partial class AdfJsonBaseTask
    {
        private readonly ApplicationOptions _appOptions;

        public AdfJsonBaseTask(IOptions<ApplicationOptions> appOptions)
        {
            _appOptions = appOptions.Value; 
        }

        public async Task ProcessTaskMaster(TaskTypeMappingProvider ttm)
        {
            //Validate TaskmasterJson based on JSON Schema
            var mappings = await ttm.GetAllActive();
            var mapping = TaskTypeMappingProvider.LookupMappingForTaskMaster(mappings,SourceSystemType, TargetSystemType, _taskMasterJsonSource["Type"].ToString(), _taskMasterJsonTarget["Type"].ToString(), TaskTypeId, TaskExecutionType);            
            string mappingSchema = mapping.TaskMasterJsonSchema;
            TaskIsValid = await JsonHelpers.ValidateJsonUsingSchema(_logging, mappingSchema, TaskMasterJson, "Failed to validate TaskMaster JSON for TaskTypeMapping: " + mapping.MappingName + ". ");
            
            if (TaskIsValid)
            {
                if (TaskType == "SQL Database to Azure Storage")
                {
                    ProcessTaskMaster_Mapping_XX_SQL_AZ_Storage_Parquet();
                    goto ProcessTaskMasterEnd;
                }

                if (TaskType == "Execute SQL Statement")
                {
                    //ProcessTaskMaster_Mapping_AZ_SQL_StoredProcedure();
                    ProcessTaskMaster_Default();
                    goto ProcessTaskMasterEnd;
                }

                if (TaskType == "Azure Storage to SQL Database")
                {
                    ProcessTaskMaster_Default();
                    JObject tgt = (JObject)_jsonObjectForAdf["Target"];                    
                    
                    string pcs = string.Format("if (object_id('[{0}].[{1}]') > 0) begin truncate table [{0}].[{1}]; end", tgt["StagingTableSchema"], tgt["StagingTableName"]) + System.Environment.NewLine;
                    if (tgt.ContainsKey("PreCopySQL"))
                    {
                        if (string.IsNullOrEmpty(tgt["PreCopySQL"].ToString()))
                        {
                            tgt["PreCopySQL"] = pcs;
                        }
                        else
                        {
                            tgt["PreCopySQL"] = pcs + tgt["PreCopySQL"];
                        }
                    }
                    else
                    {
                        tgt["PreCopySQL"] = pcs;
                    }

                    _jsonObjectForAdf["Target"] = tgt;

                    goto ProcessTaskMasterEnd;
                }

                if (TaskType == "SQL Database CDC to Azure Storage")
                {
                    ProcessTaskMaster_Mapping_SQL_CDC_AZ_Storage_Parquet();
                    goto ProcessTaskMasterEnd;
                }

                /*if (TaskType == "Execute Synapse Notebook")
                {
                    ProcessTaskMaster_SynapseNotebookExecution();
                    ProcessTaskMaster_Default();
                    goto ProcessTaskMasterEnd;
                }*/
                
                //Default Processing Branch              
                {
                    ProcessTaskMaster_Default();
                    goto ProcessTaskMasterEnd;
                }


                ProcessTaskMasterEnd:
                _logging.LogInformation("ProcessTaskMasterJson Finished");

            }
        }

        public void ProcessTaskMaster_Mapping_SQL_CDC_AZ_Storage_Parquet()
        {
            JObject source = ((JObject)_jsonObjectForAdf["Source"]) == null ? new JObject() : (JObject)_jsonObjectForAdf["Source"];
            JObject target = ((JObject)_jsonObjectForAdf["Target"]) == null ? new JObject() : (JObject)_jsonObjectForAdf["Target"];

            source.Merge(_taskMasterJson["Source"], new JsonMergeSettings
            {
                // union array values together to avoid duplicates
                MergeArrayHandling = MergeArrayHandling.Union
            });

            target.Merge(_taskMasterJson["Target"], new JsonMergeSettings
            {
                // union array values together to avoid duplicates
                MergeArrayHandling = MergeArrayHandling.Union
            });

            source["IncrementalType"] = "CDC";

            if (Helpers.JsonHelpers.CheckForJsonProperty("Instance", source) == false)
            {
                var e = new Exception("CDC Extraction Task Type does not have an instance element within taskmasterjson");
                _logging.LogErrors(e);
                throw e;
            }
            var _instance = source["Instance"];
            if ((string.IsNullOrEmpty(_instance["IncrementalValue"].ToString()) ||  _instance["IncrementalValue"].ToString().ToLower() == "no_watermark_string") && _instance["IncrementalColumnType"].ToString().ToLower() == "lsn")
            {
                source["SQLStatement"] = @$"                    
                    /*Remove First*/-- DECLARE  @from_lsn binary(10), @to_lsn binary(10);  
                    /*Remove First*/-- SET @from_lsn =sys.fn_cdc_get_min_lsn((SELECT capture_instance FROM cdc.change_tables where source_object_id  = object_id('{source["TableSchema"]}.{source["TableName"]}')));  
                    /*Remove First*/-- SET @to_lsn = sys.fn_cdc_map_time_to_lsn('largest less than or equal',  GETDATE());
                    /*Remove First*/--  SELECT CONVERT(varchar(max),@to_lsn,1) to_lsn,CONVERT(varchar(max),@from_lsn,1) from_lsn, count(*) ChangeCount FROM cdc.fn_cdc_get_all_changes_{source["TableSchema"]}_{source["TableName"]}(@from_lsn, @to_lsn, N'all');
                    /*Remove Second*/-- SELECT * FROM cdc.fn_cdc_get_all_changes_{source["TableSchema"]}_{source["TableName"]}(convert(binary(10),'/*from_lsn*/',1), convert(binary(10),'/*to_lsn*/',1), N'all');
                    ";
            }
            else if (_instance["IncrementalValue"].ToString().ToLower() != "no_watermark_string" && _instance["IncrementalColumnType"].ToString().ToLower() == "lsn")
            {
                source["SQLStatement"] = @$"
                    /*Remove First*/-- DECLARE  @from_lsn binary(10), @to_lsn binary(10);  
                    /*Remove First*/-- SET @from_lsn = {_instance["IncrementalValue"]};
                    /*Remove First*/-- SET @to_lsn = sys.fn_cdc_map_time_to_lsn('largest less than or equal',  GETDATE());
                    /*Remove First*/--  SELECT CONVERT(varchar(max),@to_lsn,1) to_lsn,CONVERT(varchar(max),@from_lsn,1) from_lsn, count(*) ChangeCount FROM cdc.fn_cdc_get_all_changes_{source["TableSchema"]}_{source["TableName"]}(@from_lsn, @to_lsn, N'all');
                    /*Remove Second*/-- SELECT * FROM cdc.fn_cdc_get_all_changes_{source["TableSchema"]}_{source["TableName"]}(convert(binary(10),'/*from_lsn*/',1), convert(binary(10),'/*to_lsn*/',1), N'all');
                    ";
            }

            _jsonObjectForAdf["Source"] = source;
            _jsonObjectForAdf["Target"] = target;

        }

        public void ProcessTaskMaster_Mapping_XX_SQL_AZ_Storage_Parquet()
        {
            JObject source = ((JObject)_jsonObjectForAdf["Source"]) == null ? new JObject() : (JObject)_jsonObjectForAdf["Source"];
            JObject target = ((JObject)_jsonObjectForAdf["Target"]) == null ? new JObject() : (JObject)_jsonObjectForAdf["Target"];

            source.Merge(_taskMasterJson["Source"], new JsonMergeSettings
            {
                // union array values together to avoid duplicates
                MergeArrayHandling = MergeArrayHandling.Union
            });

            target.Merge(_taskMasterJson["Target"], new JsonMergeSettings
            {
                // union array values together to avoid duplicates
                MergeArrayHandling = MergeArrayHandling.Union
            });

            source["IncrementalType"] = ProcessTaskMaster_Mapping_XX_SQL_AZ_Storage_Parquet_IncrementalType();
            source["IncrementalSQLStatement"] = ProcessTaskMaster_Mapping_XX_SQL_AZ_Storage_Parquet_CreateIncrementalSQLStatement(source);
            source["SQLStatement"] = ProcessTaskMaster_Mapping_XX_SQL_AZ_Storage_Parquet_CreateSQLStatement(source);


            JObject execute = new JObject();
            if (JsonHelpers.CheckForJsonProperty("StoredProcedure", _taskMasterJsonSource))
            {
                string storedProcedure = _taskMasterJsonSource["StoredProcedure"].ToString();
                if (storedProcedure.Length > 0)
                {
                    string spParameters = string.Empty;
                    if (JsonHelpers.CheckForJsonProperty("Parameters", _taskMasterJsonSource))
                    {
                        spParameters = _taskMasterJsonSource["Parameters"].ToString();
                    }
                    storedProcedure = string.Format("Exec {0} {1} {2} {3}", storedProcedure, spParameters, Environment.NewLine, " Select 1");

                }
                execute["StoredProcedure"] = storedProcedure;
            }
            source["Execute"] = execute;

            _jsonObjectForAdf["Source"] = source;
            _jsonObjectForAdf["Target"] = target;


        }

        public string ProcessTaskMaster_Mapping_XX_SQL_AZ_Storage_Parquet_IncrementalType()
        {
            string type = JsonHelpers.GetStringValueFromJson(_logging, "Type", _taskMasterJsonSource, "", true);
            if (!string.IsNullOrWhiteSpace(type))
            {
                JToken incrementalType = JsonHelpers.GetStringValueFromJson(_logging, "IncrementalType", _taskMasterJsonSource, "", true);
                int chunkSize = Convert.ToInt32(JsonHelpers.GetDynamicValueFromJson(_logging, "ChunkSize", _taskMasterJsonSource, "0", false));
                if (incrementalType.ToString() == "Full" && chunkSize == 0)
                {
                    type = "Full";
                }
                else if (incrementalType.ToString() == "Full" && chunkSize > 0)
                {
                    type = "Full_Chunk";
                }
                else if (incrementalType.ToString() == "Watermark" && chunkSize == 0)
                {
                    type = "Watermark";
                }
                else if (incrementalType.ToString() == "Watermark" && chunkSize > 0)
                {
                    type = "Watermark_Chunk";
                }
            }

            return type;
        }

        public string ProcessTaskMaster_Mapping_XX_SQL_AZ_Storage_Parquet_CreateIncrementalSQLStatement(JObject Extraction)
        {
            string sqlStatement = "";

            if (Helpers.JsonHelpers.CheckForJsonProperty("Instance", Extraction) == false)
            {
                var e = new Exception("Incremental Extraction Task Type does not have an instance element within taskmasterjson");
                _logging.LogErrors(e);
                throw e;
            }
            var _instance = Extraction["Instance"];

            string sourceType = Extraction["System"]["Type"].ToString();
            string templateFile = "";

            Dictionary<string, string> sqlParams = new Dictionary<string, string>
            {
                { "tableName", Extraction["TableName"].ToString() },
                { "tableSchema", Extraction["TableSchema"].ToString() },
            };

            string SqlTemplateLanguage = "SqlServer";
            if (sourceType == "Oracle Server")
            {
                SqlTemplateLanguage = sourceType.Replace(" ", "");
                sqlParams["tableName"] = sqlParams["tableName"].ToUpper();
                sqlParams["tableSchema"] = sqlParams["tableSchema"].ToUpper();

            }


            if (Extraction["IncrementalType"] != null)
            {

                if (Extraction["IncrementalType"].ToString().ToLower() == "full")
                {
                    sqlStatement = "";
                }

                if (Extraction["IncrementalType"].ToString().ToLower() == "full_chunk")
                {
                    templateFile = "Full_Chunk";
                    sqlParams.Add("chunkSize", Extraction["ChunkSize"].ToString());

                    /*sqlStatement = @$"
                       SELECT 
		                    CAST(CEILING(count(*)/{Extraction["ChunkSize"]} + 0.00001) as int) as  batchcount
	                    FROM [{Extraction["TableSchema"]}].[{Extraction["TableName"]}] 
                    ";*/
                }

                if (Extraction["IncrementalType"].ToString().ToLower() == "watermark" && _instance["IncrementalColumnType"].ToString().ToLower() == "datetime")
                {
                    templateFile = "WatermarkDateTime";
                    sqlParams.Add("incrementalField", _instance["IncrementalField"].ToString());
                    sqlParams.Add("incrementalValue", _instance["IncrementalValue"].ToString());


                    /*sqlStatement = @$"
                        SELECT 
	                        MAX([{_instance["IncrementalField"]}]) AS newWatermark
                        FROM 
	                        [{Extraction["TableSchema"]}].[{Extraction["TableName"]}] 
                        WHERE [{_instance["IncrementalField"]}] >= CAST('{_instance["IncrementalValue"]}' as datetime)
                    ";
                    */
                }

                if (Extraction["IncrementalType"].ToString().ToLower() == "watermark" && _instance["IncrementalColumnType"].ToString().ToLower() != "datetime")
                {

                    templateFile = "Watermark";
                    sqlParams.Add("incrementalField", _instance["IncrementalField"].ToString());
                    sqlParams.Add("incrementalValue", _instance["IncrementalValue"].ToString());

                    /*sqlStatement = @$"
                        SELECT 
	                        MAX([{_instance["IncrementalField"]}]) AS newWatermark
                        FROM 
	                        [{Extraction["TableSchema"]}].[{Extraction["TableName"]}] 
                        WHERE [{_instance["IncrementalField"]}] >= {_instance["IncrementalValue"]}
                    ";*/
                }

                if (Extraction["IncrementalType"].ToString().ToLower() == "watermark_chunk" && _instance["IncrementalColumnType"].ToString().ToLower() == "datetime")
                {

                    templateFile = "WatermarkDateTime_Chunk";
                    sqlParams.Add("incrementalField", _instance["IncrementalField"].ToString());
                    sqlParams.Add("incrementalValue", _instance["IncrementalValue"].ToString());
                    sqlParams.Add("chunkSize", Extraction["ChunkSize"].ToString());

                    /*sqlStatement = @$"
                        SELECT MAX([{_instance["IncrementalField"]}]) AS newWatermark, 
		                       CAST(CASE when count(*) = 0 then 0 else CEILING(count(*)/{Extraction["ChunkSize"]} + 0.00001) end as int) as  batchcount
	                    FROM  [{Extraction["TableSchema"]}].[{Extraction["TableName"]}] 
	                    WHERE [{_instance["IncrementalField"]}] >= CAST('{_instance["IncrementalValue"]}' as datetime)
                    "; */
                }

                if (Extraction["IncrementalType"].ToString().ToLower() == "watermark_chunk" && _instance["IncrementalColumnType"].ToString().ToLower() != "datetime")
                {

                    templateFile = "Watermark_Chunk";
                    sqlParams.Add("incrementalField", _instance["IncrementalField"].ToString());
                    sqlParams.Add("incrementalValue", _instance["IncrementalValue"].ToString());
                    sqlParams.Add("chunkSize", Extraction["ChunkSize"].ToString());

                    /*sqlStatement = @$"
                        SELECT MAX([{_instance["IncrementalField"]}]) AS newWatermark, 
		                       CAST(CASE when count(*) = 0 then 0 else CEILING(count(*)/{Extraction["ChunkSize"]} + 0.00001) end as int) as  batchcount
	                    FROM  [{Extraction["TableSchema"]}].[{Extraction["TableName"]}] 
	                    WHERE [{_instance["IncrementalField"]}] >= {_instance["IncrementalValue"]}
                    ";*/
                }


            }

            if (!string.IsNullOrEmpty(templateFile))
            {
                sqlStatement = GenerateSqlStatementTemplates.GetSql(System.IO.Path.Combine(EnvironmentHelper.GetWorkingFolder(), _appOptions.LocalPaths.SQLTemplateLocation), "CreateIncrementalSQLStatement_" + templateFile + "_" + SqlTemplateLanguage, sqlParams);
            }

            return sqlStatement;
        }

        public string ProcessTaskMaster_Mapping_XX_SQL_AZ_Storage_Parquet_CreateSQLStatement(JObject Extraction)
        {
            string sqlStatement = "";

            string incrementalType = ((string)Extraction["IncrementalType"]).ToLower();
            Int32 chunkSize = (Int32)Extraction["ChunkSize"];
            JToken incrementalField = JsonHelpers.GetStringValueFromJson(_logging, "IncrementalField", _taskInstanceJson, "", false);
            JToken incrementalColumnType = JsonHelpers.GetStringValueFromJson(_logging, "IncrementalColumnType", _taskInstanceJson, "", false);
            JToken chunkField = (string)Extraction["ChunkField"];
            JToken tableSchema = Extraction["TableSchema"];
            JToken tableName = Extraction["TableName"];
            string sourceType = Extraction["System"]["Type"].ToString();

            string extractionSql = JsonHelpers.GetStringValueFromJson(_logging, "ExtractionSQL", Extraction, "", false);
            string templateFile = "";
            Dictionary<string, string> sqlParams = new Dictionary<string, string>
            {
                { "tableName", tableName.ToString() },
                { "tableSchema", tableSchema.ToString() },
                { "incrementalField", incrementalField.ToString() }
            };



            string SqlTemplateLanguage = "SqlServer";
            if (sourceType == "Oracle Server")
            {
                SqlTemplateLanguage = sourceType.Replace(" ", "");
                sqlParams["tableName"] = sqlParams["tableName"].ToUpper();
                sqlParams["tableSchema"] = sqlParams["tableSchema"].ToUpper();
            }

            //If Extraction SQL Explicitly set then overide _SQLStatement with that explicit value
            if (!string.IsNullOrWhiteSpace(extractionSql))
            {
                sqlStatement = extractionSql;                

                if (incrementalType == "full_chunk" || incrementalType == "watermark_chunk")
                {
                    if (incrementalType == "full_chunk")
                    {   
                        sqlStatement = sqlStatement.Replace("{chunkField}", chunkField.ToString());
                    }

                    if (incrementalType == "watermark_chunk")
                    {   
                        sqlStatement = sqlStatement.Replace("{chunkField}", chunkField.ToString());
                        sqlStatement = sqlStatement.Replace("{incrementalField}", incrementalField.ToString());                        
                        
                        if (incrementalColumnType.ToString() == "DateTime")
                        {
                            DateTime incrementalValueDateTime = (DateTime)_taskInstanceJson["IncrementalValue"];                            
                            if (sourceType == "Oracle Server")
                            {
                                sqlStatement = sqlStatement.Replace("{incrementalValueDateTime}",incrementalValueDateTime.ToString("yyyy-MM-dd HH:mm:ss.ff"));
                            }
                            else
                            {
                                sqlStatement = sqlStatement.Replace("{incrementalValueDateTime}",incrementalValueDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));                                
                            }                
                        }
                        else if (incrementalColumnType.ToString() == "BigInt")
                        {
                            int incrementalValueBigInt = (int)_taskInstanceJson["IncrementalValue"];
                            sqlStatement = sqlStatement.Replace("{incrementalValueBigInt}",incrementalValueBigInt.ToString());      
                        }
                    }
                }  
                else //Non Chunk 
                {
                    if (incrementalType == "watermark")
                    {
                        sqlStatement = sqlStatement.Replace("{incrementalField}", incrementalField.ToString());                        
                        
                        if (incrementalColumnType.ToString() == "DateTime")
                        {
                            DateTime incrementalValueDateTime = (DateTime)_taskInstanceJson["IncrementalValue"];                            
                            if (sourceType == "Oracle Server")
                            {
                                sqlStatement = sqlStatement.Replace("{incrementalValueDateTime}",incrementalValueDateTime.ToString("yyyy-MM-dd HH:mm:ss.ff"));
                            }
                            else
                            {
                                sqlStatement = sqlStatement.Replace("{incrementalValueDateTime}",incrementalValueDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));                                
                            }                
                        }
                        else if (incrementalColumnType.ToString() == "BigInt")
                        {
                            int incrementalValueBigInt = (int)_taskInstanceJson["IncrementalValue"];
                            sqlStatement = sqlStatement.Replace("{incrementalValueBigInt}",incrementalValueBigInt.ToString());      
                        }
                    }

                }                    
                
                goto EndOfSQLStatementSet;

            }

            //Chunk branch
            if (incrementalType == "full_chunk" || incrementalType == "watermark_chunk")
            {               
                if (incrementalType == "full_chunk")
                {
                    templateFile = "Full_Chunk";
                    sqlParams.Add("chunkField", chunkField.ToString());
                }                
                else if (incrementalType == "watermark_chunk" && !string.IsNullOrWhiteSpace(_taskMasterJsonSource["Source"]["ChunkSize"].ToString()))
                {
                    if (incrementalColumnType.ToString() == "DateTime")
                    {
                        DateTime incrementalValueDateTime = (DateTime)_taskInstanceJson["IncrementalValue"];
                        templateFile = "WatermarkDateTime_Chunk";
                        if (sourceType == "Oracle Server")
                        {
                            sqlParams.Add("incrementalValueDateTime", incrementalValueDateTime.ToString("yyyy-MM-dd HH:mm:ss.ff"));
                        }
                        else
                        {
                            sqlParams.Add("incrementalValueDateTime", incrementalValueDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        }                
                    }
                    else if (incrementalColumnType.ToString() == "BigInt")
                    {
                        int incrementalValueBigInt = (int)_taskInstanceJson["IncrementalValue"];
                        templateFile = "WatermarkBigInt_Chunk";
                        sqlParams.Add("incrementalValueBigInt", incrementalValueBigInt.ToString()); }
                    sqlParams.Add("chunkField", chunkField.ToString());

                }
            }
            else
            //Non Chunk
            {
                if (incrementalType == "full")
                {
                    templateFile = "Full";                    
                }                                
                else if (incrementalType == "watermark")
                {
                    if (incrementalColumnType.ToString() == "DateTime")
                    {
                        DateTime incrementalValueDateTime = (DateTime)_taskInstanceJson["IncrementalValue"];
                        templateFile = "WatermarkDateTime";
                        if (sourceType == "Oracle Server")
                        {
                            sqlParams.Add("incrementalValueDateTime", incrementalValueDateTime.ToString("yyyy-MM-dd HH:mm:ss.ff"));
                        }
                        else
                        {
                            sqlParams.Add("incrementalValueDateTime", incrementalValueDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        }
                    }
                    else if (incrementalColumnType.ToString() == "BigInt")
                    {
                        int incrementalValueBigInt = (int)_taskInstanceJson["IncrementalValue"];
                        templateFile = "WatermarkBigInt";
                        sqlParams.Add("incrementalValueBigInt", incrementalValueBigInt.ToString());
                    }
                }
            }
        
        sqlStatement = GenerateSqlStatementTemplates.GetSql(System.IO.Path.Combine(EnvironmentHelper.GetWorkingFolder(), _appOptions.LocalPaths.SQLTemplateLocation), "CreateSQLStatement_" + templateFile + "_" + SqlTemplateLanguage, sqlParams);
        
        EndOfSQLStatementSet:
            if (string.IsNullOrWhiteSpace(sqlStatement))
            {
                Exception e = new Exception("SqlStatement for Extraction has not been set. IncrementalType = " + incrementalType);
                _logging.LogErrors(e);
                throw e;
            }
        
        return sqlStatement;
        }

        public void ProcessTaskMaster_Mapping_AZ_SQL_StoredProcedure()
        {
            JObject source = (JObject)_jsonObjectForAdf["Source"];
            JObject execute = new JObject();
            execute["StoredProcedure"] =
                $"Exec {_taskMasterJsonSource["Source"]["StoredProcedure"]} {_taskMasterJsonSource["Source"]["Parameters"]} {Environment.NewLine}  Select 1";

            source["Execute"] = execute;
            _jsonObjectForAdf["Source"] = source;

        }


        /// <summary>
        /// Default Method which merges Source & Target attributes on TaskMasterJson with existing Source and Target Attributes on TaskObject payload.
        /// </summary>

        public void ProcessTaskMaster_Default()
        {            
            var source = (JObject)_jsonObjectForAdf["Source"] ?? new JObject();
            var target = (JObject)_jsonObjectForAdf["Target"] ?? new JObject();
           
            


            source.Merge(_taskMasterJson["Source"], new JsonMergeSettings
            {
                // union array values together to avoid duplicates
                MergeArrayHandling = MergeArrayHandling.Union
            });

            target.Merge(_taskMasterJson["Target"], new JsonMergeSettings
            {
                // union array values together to avoid duplicates
                MergeArrayHandling = MergeArrayHandling.Union
            });



            _jsonObjectForAdf["Source"] = source;
            _jsonObjectForAdf["Target"] = target;

            var rootAttributes = _taskMasterJson;
            rootAttributes.Remove("Source");
            rootAttributes.Remove("Target");

            _jsonObjectForAdf["TMOptionals"] = rootAttributes;
        }
    }
}
