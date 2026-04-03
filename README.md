# ResourceModLoader

## 这是什么？

> Addressable是unity的一套资产管理系统。可以用来通过名称快速获取资产。
>
> 本项目通过修改catalog.json，实现让游戏从不同位置读取可寻址资源。

本项目为`吉星派对`设计，该方案可能适用于其他游戏，但是该程序是针对这一游戏的数据结构设计的。

## 有没有省流？

自动识别并加载各种替换资产类的“mod”，用方便的方式管理他们。

你可以将各种替换资产的mod拖放到mods文件夹，执行一次resourceModLoader便可使他们生效。删除mod文件后再次执行即可恢复原样。

将本项目的exe文件（可选另一个dll）放到游戏根目录并双击打开即可加载mods文件夹

## 这个项目能做到什么？

任意修改使用Addressable的资产，可以在一个bundle内修改多个资产。目前只能做到从其他AB中读取。

## 如何使用？

1. 将项目放置到游戏可执行文件目录（或者其中一个子目录）
```
|-AstralParty_CN.exe
|-modloader
|---resourceModLoader.exe
|---...._
```
或者

```
|-AstralParty_CN.exe
|-resourceModLoader.exe
```
如果你需要加载图片作为替换项目，你还需要下载`PVRTexLib.dll`
2. 运行一次程序或者手动创建mods文件夹
3. 将AB/zip文件/图片文件放入mods文件夹
4. 执行resourceModLoader.exe

## 支持哪些替换？

mods目录会被递归读取，你可以创建任意多的文件夹。

### 直接AB替换
将游戏中的AB提取出来，修改后放置到mods文件夹。程序会自动检测相对于原mod的变化并生成重定向。

### replace.txt
将资产打包成AB后，放在mods/<any path>/xxxx.bundle
创建replace.txt，内容如下

```
#每行表示一个重定向
原始文件名:bundle文件名:目标bundle文件名
#目标bundle文件名代表原来的资产所在的bundle文件名，可以省略以使用相同的文件名
原始文件名:bundle文件名
```

### 图片
对于图片类型的Addressable，可以直接将图片命名成文件名.png/.jpg，程序会自动将其打包为AB并设置重定向

### 精灵动画

需要先安装 [](URL "https://mirrors.tuna.tsinghua.edu.cn/python/3.12.10/python-3.12.10.exe")，其余库会在运行时自动安装。

` ResourceModLoader.exe tool sprite-anim import/export ` 命令后会在mod旁新建import/export文件夹，import内放入含sprite动画的资源包后，使用`export`命令即可在export内解包，在export内修改后 使用`import`命令即可生成新_patcher.ab包 

### zip
自动解压后识别上述类型