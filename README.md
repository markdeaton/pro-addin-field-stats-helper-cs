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
