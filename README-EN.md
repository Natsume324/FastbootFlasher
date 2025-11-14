<a href="https://github.com/Natsume324/FastbootFlasher/blob/master/README.md">查看中文说明</a>

<div id="header" align="center">
	<img src="https://raw.githubusercontent.com/Natsume324/FastbootFlasher/refs/heads/master/logo.png" ></img> 
	<h1>Madara•FastbootFlasher</h1>
	<div id="badges" >
		<a href="https://github.com/Natsume324/FastbootFlasher/blob/master/LICENSE"><img src="https://img.shields.io/github/license/Uotan-Dev/UotanToolboxNT" alt="License"/></a>
		<a href="https://qm.qq.com/q/FzaVgZu1O0"><img src="https://img.shields.io/badge/QQ%20Group-4379c4" alt="License"/></a>
		<a href="https://t.me/FastbootFlasher"><img src="https://img.shields.io/badge/Chat-Telegram-brightgreen.svg?logo=telegram&style=flat-square&style=for-the-badge" alt="License"/></a>
	</div>
</div>
<br/>

## Features
- Support for multiple firmware types
- Bilingual interface (Chinese/English)
- Pure, lightweight and ad-free
- Completely offline operation

## Firmware support
Currently supported firmware:  

Huawei: UPDATE.APP OTA update file  Harmony NEXT update.bin OTA update file

Android: Standard OTA firmware payload.bin (including Xiaomi, Redmi, OPPO, OnePlus, Realme and other OTA firmware) .img image file  

Xiaomi: Official flash_all.bat script

## Function
Currently supported functions:  

Huawei: parse and extract partition images in .APP/update.bin files, merge dual super partitions, select partitions to flash, flash success and failure statistics, read information in fasboot mode, jump to upgrade mode in fastboot mode  

Android: parse and extract partition images in payload.bin firmware, select partitions to flash, flash success and failure statistics, read BL lock status, execute unlock BL command  
Select multiple .img image files to flash, the partition to be flashed is determined by the file name

Xiaomi: parse the flash command in the flash_all.bat script of Xiaomi official online flash firmware, select partitions to flash, flash success and failure statistics  

## Screenshots

![Homepage](https://i.ibb.co/9HRGbzqS/Main-en.png)

![Function](https://i.ibb.co/j9LWKDNp/Func-en.png)

![About](https://i.ibb.co/DH0kvX11/About-en.png)

# 🛑 Disclaimer
This software is provided for educational and research purposes only.
Using this software to flash firmware, unlock devices, or perform other operations may result in device malfunction, data loss, or warranty void.
You are fully responsible for any consequences arising from the use of this software.
The author shall not be held liable for any damage, legal issues, or losses caused by using this software.
The author does not support or encourage any use of this project for illegal or criminal purposes.

