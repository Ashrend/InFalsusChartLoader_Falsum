## `if` 文件示例：

### 基础版
```json
{
  "id": "20260929052150xingwei",              //谱面id，需要尽可能的保证它是唯一的，若重复则不加载谱面
  "name": "music",                            //曲名
  "composer": "I don’t know",                 //曲师
  "song": "music.mp3",                        //歌曲文件，接受ogg、mp3、wav
  "illust": "I don’t know",                   //曲绘画师；为可选参数
  "jacket": "jkt.png",                        //曲绘，只接受png，建议为正方形
  "background": "bg.mp4",                     //谱面内的背景，接受png与mp4；为可选参数
  "preview_seconds": ["0", "60"],             //预览音频时间[开始,结束]，以秒计算
  "charter": "Xing_W",                        //谱师
  "chart": "chart.spc",                       //谱面文件，不兼容demo格式
  "lv": 15                                    //谱面难度
}
```
谱面位于Forbidden难度

### 拓展版
```json
{
  "id": "20260929052150xingwei",
  //谱面id，需要尽可能的保证它是唯一的，若重复则不加载谱面
  "name": "music",
  //曲名
  "composer": "I don’t know",
  //曲师
  "song": "music.mp3",
  //歌曲文件，接受ogg、mp3、wav
  "illust": ["I don’t know!", null, null, "hey?"],
  //曲绘画师[Minimal,Evolved,Ultimate,Forbidden]，
  //可用null占位（占位后当前等级不展示画师）；为可选参数
  "jacket": ["jkt.png", "jtk.png", "kjt.png", "ktj.png"],
  //曲绘[Minimal,Evolved,Ultimate,Forbidden]，只接受png，建议为正方形
  "background": [null, null, "bg.png", "bg.mp4"],
  //谱面内的背景[Minimal,Evolved,Ultimate,Forbidden]，接受png与mp4，
  //可用null占位（占位后当前等级回归默认背景）；为可选参数
  "preview_seconds": ["0", "60"],
  //预览音频时间[开始,结束]，以秒计算
  "charter": ["Skytarry", "MagicNeko", null, "Xing_W"],
  //谱师[Minimal,Evolved,Ultimate,Forbidden]
  //对应难度的谱面为null的情况下可用null占位
  "chart": [null, null, null, "chart.spc"],
  //谱面文件，不兼容demo格式[Minimal,Evolved,Ultimate,Forbidden]
  //可用null占位
  "lv": [1, null, 1, 15]
  //谱面难度[Minimal,Evolved,Ultimate,Forbidden]
  //对应难度的谱面为null的情况下可用null占位
}
```
拓展说明：
```json
"chart": "chart.spc", "charter": "Xing_W", "lv": 15
```
等同于
```json
"chart": [null, null, null, "chart.spc"],
"charter": [null, null, null, "Xing_W"],
"lv": [null, null, null, 15]
```
因此，以下为写法合法：
```json
{
  "charter": ["Skytarry", "MagicNeko", null, "Xing_W"],
  "chart": "chart.spc",
  "lv": 15
}
{
  "charter": ["Skytarry", "MagicNeko", null, "Xing_W"],
  "chart": "chart.spc",
  "lv": [1, null, 1, 15]
}
{
  "charter": "Xing_W",
  "chart": "chart.spc",
  "lv": [1, null, 1, 15]
}
```

## 文件夹示例：
```
Charts
      `-- chart
          |-- bg.mp4
          |-- bg.png
          |-- chart.spc
          |-- dummy.spc
          |-- if
          |-- jkt.png
          |-- jtk.png
          |-- kjt.png
          |-- ktj.png
          |-- music.mp3
          |-- temp.spc
          `-- untitled.spc
```