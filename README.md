# pro-addin-field-stats-helper-cs
An add-in for ArcGIS Pro to compute and display simple statistics for a selected numerical field.
Field types currently supported are:
<ul>
<li>integer</li>
<li>smallinteger</li>
<li>single</li>
<li>double</li>
</ul>

Statistics shown are:
<ul>
<li>min</li>
<li>max</li>
<li>mean</li>
<li>median</li>
<li>standard deviation</li>
</ul>

Also, because the algorithm finds and discards null values before calculating statistics, the user interface also tells you how many null values it had to discard in that field.

A histogram shows how many values for each of 25 value ranges are in the field's data.

[6/28/2017]
There is now a "Pro_2.0" branch for use with ArcGIS Pro version 2. The only coding change needed was in the .daml file to specify Pro 2.0 as a target. There are also compiled versions you can download and use:

ArcGIS Pro 2: https://www.arcgis.com/home/item.html?id=f15706a037b0462abc930f9bb43d450b

ArcGIS Pro 1.4: https://www.arcgis.com/home/item.html?id=3f7b7d203b164304989adb88ebf88ed0
