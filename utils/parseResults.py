# %%
import pandas as pd
import os
import pathlib
import matplotlib.pyplot as plt

# %%
resultDate = "2025_05_12__21_44"

csvsPath = f"../src/EntityFX.MqttSimulator/EntityFX.MqttY.Cli/bin/Release/net6.0/results/{resultDate}"

cwd = os.getcwd()

csvFiles = []
for file in pathlib.Path(csvsPath).rglob('*.csv'):
    csvFiles.append(file)

if not os.path.isdir(resultDate):
    os.mkdir(resultDate)

# %%

for csv in csvFiles:
    df = pd.read_csv(csv, delimiter=';',decimal='.', encoding="utf8")
    df.head()

    header = str(csv).replace('\\','/').replace(csvsPath, ' ')
    plotPictureName = f"{header.replace('/','_')}.png"

    fig = plt.figure()
    ax = fig.add_subplot()
    fig.subplots_adjust(top=0.85)
    ax.set_title(header)
    ax.set_xlabel(df.columns[0])
    ax.set_ylabel(df.columns[1])

    plt.figure(figsize=(3, 2))#Graph size
    ax.plot(df[df.columns[0]], df[df.columns[1]])
    fig.savefig(f"{resultDate}/{plotPictureName}")
    #plt.show()





