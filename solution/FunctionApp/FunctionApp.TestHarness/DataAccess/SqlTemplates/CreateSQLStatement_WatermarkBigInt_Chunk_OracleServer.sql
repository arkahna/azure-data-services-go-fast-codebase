﻿SELECT * FROM {tableSchema}.{tableName} WHERE {incrementalField} > Cast('{incrementalValueBigInt}' as LONG) AND {incrementalField} <= Cast('<newWatermark>' as LONG) AND CAST({chunkField} AS LONG) %  <batchcount> = <item> -1.