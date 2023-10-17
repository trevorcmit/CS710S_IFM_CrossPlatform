using Acr.UserDialogs;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Extensions;
using Prism.Mvvm;
using Plugin.Share;
using Plugin.Share.Abstractions;
using MvvmCross.ViewModels;
using MvvmCross;
using MvvmCross.Navigation;
using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Windows.Input;
using Xamarin.Forms;
using Xamarin.Essentials;


namespace BLE.Client.ViewModels
{
    public class ViewModelRFMicroS3Inventory : BaseViewModel
    {
        public class RFMicroTagInfoViewModel : BindableBase
        {
            private string _EPC;
            public string EPC { get { return this._EPC; } set { this.SetProperty(ref this._EPC, value); } }

            private string _NickName;
            public string NickName { get { return this._NickName; } set { this.SetProperty(ref this._NickName, value); } }

            private string _DisplayName;
            public string DisplayName { get { return this._DisplayName; } set { this.SetProperty(ref this._DisplayName, value); } }

            private uint _OCRSSI;
            public uint OCRSSI { get { return this._OCRSSI; } set { this.SetProperty(ref this._OCRSSI, value); } }

            public double _sensorValueSum;
            private string _sensorAvgValue;
            public string SensorAvgValue { get { return this._sensorAvgValue; } set { this.SetProperty(ref this._sensorAvgValue, value); } }

            private uint _sucessCount;
            public uint SucessCount { get { return this._sucessCount; } set { this.SetProperty(ref this._sucessCount, value); } }
            public RFMicroTagInfoViewModel() { }
        }

        private readonly IUserDialogs _userDialogs;
        private readonly IMvxNavigationService _navigation;

        #region -------------- RFID inventory -----------------

        public ICommand OnStartInventoryButtonCommand { protected set; get; }
        public ICommand OnClearButtonCommand { protected set; get; }
        public ICommand OnShareDataCommand { protected set; get; }
        
        private ObservableCollection<RFMicroTagInfoViewModel> _TagInfoList = new ObservableCollection<RFMicroTagInfoViewModel>();
        public ObservableCollection<RFMicroTagInfoViewModel> TagInfoList { get { return _TagInfoList; } set { SetProperty(ref _TagInfoList, value); } }

        private string _startInventoryButtonText = "Start Inventory";
        public string startInventoryButtonText { get { return _startInventoryButtonText; } }

        private string _labelVoltage = "";
        public string labelVoltage { get { return _labelVoltage; } }
        public bool _startInventory = true;
        DateTime InventoryStartTime;

        private string _currentPower;
        public string currentPower { get { return _currentPower; } set { _currentPower = value; } }

        bool _cancelVoltageValue = false;

        public FileResult pick_result;  // Save FilePicker.PickAsync() result for use in Autosave function
        public FileResult rssi_result;  

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ViewModelRFMicroS3Inventory(IAdapter adapter, IUserDialogs userDialogs, IMvxNavigationService navigation) : base(adapter)
        {
            _userDialogs = userDialogs;
            _navigation = navigation;

            OnStartInventoryButtonCommand = new Command(StartInventoryClick);
            OnClearButtonCommand = new Command(ClearClick);
            OnShareDataCommand = new Command(ShareDataButtonClick);

            SetPowerString();
        }

        ~ViewModelRFMicroS3Inventory() { }

        public override void ViewAppearing()
        {
            base.ViewAppearing();

            // RFID event handler
            BleMvxApplication._reader.rfid.OnAsyncCallback += new EventHandler<CSLibrary.Events.OnAsyncCallbackEventArgs>(TagInventoryEvent);

            // Key Button event handler
            BleMvxApplication._reader.notification.OnKeyEvent += new EventHandler<CSLibrary.Notification.HotKeyEventArgs>(HotKeys_OnKeyEvent);
            BleMvxApplication._reader.notification.OnVoltageEvent += new EventHandler<CSLibrary.Notification.VoltageEventArgs>(VoltageEvent);

            InventorySetting();
        }

        public override void ViewDisappearing()
        {
            BleMvxApplication._reader.rfid.CancelAllSelectCriteria();                // Confirm cancel all filter

            BleMvxApplication._reader.rfid.StopOperation();
            ClassBattery.SetBatteryMode(ClassBattery.BATTERYMODE.IDLE);
            BleMvxApplication._reader.barcode.Stop();

            // Cancel RFID event handler
            BleMvxApplication._reader.rfid.OnAsyncCallback -= new EventHandler<CSLibrary.Events.OnAsyncCallbackEventArgs>(TagInventoryEvent);
            BleMvxApplication._reader.rfid.OnStateChanged += new EventHandler<CSLibrary.Events.OnStateChangedEventArgs>(StateChangedEvent);

            // Key Button event handler
            BleMvxApplication._reader.notification.OnKeyEvent -= new EventHandler<CSLibrary.Notification.HotKeyEventArgs>(HotKeys_OnKeyEvent);
            BleMvxApplication._reader.notification.OnVoltageEvent -= new EventHandler<CSLibrary.Notification.VoltageEventArgs>(VoltageEvent);

            base.ViewDisappearing();
        }

        protected override void InitFromBundle(IMvxBundle parameters)
        {
            base.InitFromBundle(parameters);
        }

        private void ClearClick()
        {
            // InvokeOnMainThread(() =>
            // {
            //     lock (TagInfoList)
            //     {
            //         TagInfoList.Clear();
            //         _numberOfTagsText = _TagInfoList.Count.ToString() + " tags";
            //         RaisePropertyChanged(() => numberOfTagsText);

            //         tagsCount = 0;
            //         _tagPerSecondText = tagsCount.ToString() + " tags/s";
            //         RaisePropertyChanged(() => tagPerSecondText);
            //     }
            // });
        }

        public RFMicroTagInfoViewModel objItemSelected
        {
            set
            {
                if (value != null)
                {
                    BleMvxApplication._SELECT_EPC = value.EPC;
                    //ShowViewModel<ViewModelRFMicroReadTemp>(new MvxBundle());
                    _navigation.Navigate<ViewModelRFMicroReadTemp>(new MvxBundle());
                }
            }
            get => null;
        }

        void InventorySetting()
        {
            BleMvxApplication._reader.rfid.SetCountry(BleMvxApplication._config.RFID_Region, (int)BleMvxApplication._config.RFID_FixedChannel);
            BleMvxApplication._reader.rfid.Options.TagRanging.flags = CSLibrary.Constants.SelectFlags.ZERO;

            // BleMvxApplication._reader.rfid.SetInventoryDuration(BleMvxApplication._config.RFID_Antenna_Dwell);
            // BleMvxApplication._reader.rfid.SetTagDelayTime((uint)BleMvxApplication._config.RFID_CompactInventoryDelayTime); // for CS108 only
            // BleMvxApplication._reader.rfid.SetIntraPacketDelayTime((uint)BleMvxApplication._config.RFID_IntraPacketDelayTime); // for CS710S only
            // BleMvxApplication._reader.rfid.SetDuplicateEliminationRollingWindow(BleMvxApplication._config.RFID_DuplicateEliminationRollingWindow); // for CS710S only
            // BleMvxApplication._reader.rfid.SetCurrentLinkProfile(BleMvxApplication._config.RFID_Profile);
            // BleMvxApplication._reader.rfid.SetTagGroup(BleMvxApplication._config.RFID_TagGroup);

            // Setting 1
            SetPower(BleMvxApplication._rfMicro_Power);

            // Setting 3  // MUST SET for RFMicro
            BleMvxApplication._config.RFID_DynamicQParms.toggleTarget = (BleMvxApplication._rfMicro_Target == 2) ? 1U : 0U;
            BleMvxApplication._reader.rfid.SetDynamicQParms(BleMvxApplication._config.RFID_DynamicQParms);

            // Setting 4
            BleMvxApplication._config.RFID_FixedQParms.toggleTarget = (BleMvxApplication._rfMicro_Target == 2) ? 1U : 0U;
            BleMvxApplication._reader.rfid.SetFixedQParms(BleMvxApplication._config.RFID_FixedQParms);

            // Setting 2
            BleMvxApplication._reader.rfid.SetOperationMode(BleMvxApplication._config.RFID_OperationMode);
            BleMvxApplication._reader.rfid.SetTagGroup(CSLibrary.Constants.Selected.ASSERTED, BleMvxApplication._config.RFID_TagGroup.session, (BleMvxApplication._rfMicro_Target != 1) ? CSLibrary.Constants.SessionTarget.A : CSLibrary.Constants.SessionTarget.B);
            BleMvxApplication._reader.rfid.SetCurrentSingulationAlgorithm(BleMvxApplication._config.RFID_Algorithm);
            BleMvxApplication._reader.rfid.SetCurrentLinkProfile(BleMvxApplication._config.RFID_Profile);

            // Select RFMicro S3 filter
            {
                CSLibrary.Structures.SelectCriterion extraSlecetion = new CSLibrary.Structures.SelectCriterion();

                extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.ASLINVA_DSLINVB, 0);
                extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.TID, 0, 28, new byte[] { 0xe2, 0x82, 0x40, 0x30 });
                BleMvxApplication._reader.rfid.SetSelectCriteria(0, extraSlecetion);

                // Set OCRSSI Limit
                extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.NOTHING_DSLINVB, 0);
                extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.BANK3, 0xd0, 8, new byte[] { (byte)(0x20 | BleMvxApplication._rfMicro_minOCRSSI) });
                BleMvxApplication._reader.rfid.SetSelectCriteria(1, extraSlecetion);

                extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.NOTHING_DSLINVB, 0);
                extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.BANK3, 0xd0, 8, new byte[] { (byte)(BleMvxApplication._rfMicro_maxOCRSSI) });
                BleMvxApplication._reader.rfid.SetSelectCriteria(2, extraSlecetion);

                // Temperature and Sensor code
                extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.NOTHING_DSLINVB, 0);
                extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.BANK3, 0xe0, 0, new byte[] { 0x00 });
                BleMvxApplication._reader.rfid.SetSelectCriteria(3, extraSlecetion);

                BleMvxApplication._reader.rfid.Options.TagRanging.flags |= CSLibrary.Constants.SelectFlags.SELECT;
            }

            // Multi bank inventory
            BleMvxApplication._reader.rfid.Options.TagRanging.multibanks = 2;
            BleMvxApplication._reader.rfid.Options.TagRanging.bank1 = CSLibrary.Constants.MemoryBank.BANK0;
            BleMvxApplication._reader.rfid.Options.TagRanging.offset1 = 12; // address C
            BleMvxApplication._reader.rfid.Options.TagRanging.count1 = 3;
            BleMvxApplication._reader.rfid.Options.TagRanging.bank2 = CSLibrary.Constants.MemoryBank.USER;
            BleMvxApplication._reader.rfid.Options.TagRanging.offset2 = 8;
            BleMvxApplication._reader.rfid.Options.TagRanging.count2 = 4;
            BleMvxApplication._reader.rfid.Options.TagRanging.compactmode = false;

            BleMvxApplication._reader.rfid.StartOperation(CSLibrary.Constants.Operation.TAG_PRERANGING);
        }

        void SetPowerString ()
        {
            string[] _powerOptions = { "Low (16dBm)", "Mid (23dBm)", "High (30dBm)", "Auto ", "Follow System Setting" };

            if (BleMvxApplication._rfMicro_Power == 3)
                currentPower = "Auto " + _powerOptions[_powerRunning];
            else
                currentPower = _powerOptions[BleMvxApplication._rfMicro_Power];

            RaisePropertyChanged(() => currentPower);
        }

        void SetConfigPower()
        {
            if (BleMvxApplication._reader.rfid.GetAntennaPort() == 1)
            {
                if (BleMvxApplication._config.RFID_PowerSequencing_NumberofPower == 0)
                {
                    BleMvxApplication._reader.rfid.SetPowerSequencing(0);
                    BleMvxApplication._reader.rfid.SetPowerLevel(BleMvxApplication._config.RFID_Antenna_Power[0]);
                }
                else
                    BleMvxApplication._reader.rfid.SetPowerSequencing(BleMvxApplication._config.RFID_PowerSequencing_NumberofPower, BleMvxApplication._config.RFID_PowerSequencing_Level, BleMvxApplication._config.RFID_PowerSequencing_DWell);
            }
            else
            {
                for (uint cnt = BleMvxApplication._reader.rfid.GetAntennaPort() - 1; cnt >= 0; cnt--)
                {
                    BleMvxApplication._reader.rfid.SetPowerLevel(BleMvxApplication._config.RFID_Antenna_Power[cnt], cnt);
                }
            }
        }

        void SetPower(int index)
        {
            switch (index)
            {
                case 0:
                    BleMvxApplication._reader.rfid.SetPowerSequencing(0);
                    BleMvxApplication._reader.rfid.SetPowerLevel(160);
                    break;
                case 1:
                    BleMvxApplication._reader.rfid.SetPowerSequencing(0);
                    BleMvxApplication._reader.rfid.SetPowerLevel(230);
                    break;
                case 2:
                    BleMvxApplication._reader.rfid.SetPowerSequencing(0);
                    BleMvxApplication._reader.rfid.SetPowerLevel(300);
                    break;
                case 3:
                    SetPower(_powerRunning);
                    break;
                case 4:
                    SetConfigPower();
                    break;
            }
        }

        int _powerRunning = 0;
        void StartInventory()
        {
            if (_startInventory == false)
                return;

            SetPower(BleMvxApplication._rfMicro_Power);

            //if (BleMvxApplication._config.RFID_OperationMode == CSLibrary.Constants.RadioOperationMode.CONTINUOUS)
            {
                _startInventory = false;
                _startInventoryButtonText = "Stop Inventory";
            }

            InventoryStartTime = DateTime.Now;
            BleMvxApplication._reader.rfid.StartOperation(CSLibrary.Constants.Operation.TAG_EXERANGING);
            ClassBattery.SetBatteryMode(ClassBattery.BATTERYMODE.INVENTORY);
            _cancelVoltageValue = true;

            RaisePropertyChanged(() => startInventoryButtonText);
        }

        void StopInventory()
        {
            _startInventory = true;
            _startInventoryButtonText = "Start Inventory";

            BleMvxApplication._reader.rfid.StopOperation();
            RaisePropertyChanged(() => startInventoryButtonText);
        }

        void StartInventoryClick()
        {
            if (_startInventory)
            {
                StartInventory();
            }
            else
            {
                StopInventory();
            }
        }

        void TagInventoryEvent(object sender, CSLibrary.Events.OnAsyncCallbackEventArgs e)
        {
            if (e.type != CSLibrary.Constants.CallbackType.TAG_RANGING)
                return;

            if (e.info.Bank1Data == null || e.info.Bank2Data == null)
                return;

            InvokeOnMainThread(() =>
            {
                // AddOrUpdateTagData(e.info);
                updatetagdata(e.info);
            });
        }

        void StateChangedEvent(object sender, CSLibrary.Events.OnStateChangedEventArgs e)
        {
            switch (e.state)
            {
                case CSLibrary.Constants.RFState.IDLE:
                    ClassBattery.SetBatteryMode(ClassBattery.BATTERYMODE.IDLE);
                    _cancelVoltageValue = true;
                    switch (BleMvxApplication._reader.rfid.LastMacErrorCode)
                    {
                        case 0x00:  // normal end
                            break;

                        case 0x0309:    // 
                            _userDialogs.Alert("Too near to metal, please move CS108 away from metal and start inventory again.");
                            break;

                        default:
                            _userDialogs.Alert("Mac error : 0x" + BleMvxApplication._reader.rfid.LastMacErrorCode.ToString("X4"));
                            break;
                    }
                    break;
            }
        }

        private async void updatetagdata(CSLibrary.Structures.TagCallbackInfo info)
        {
            InvokeOnMainThread(() =>
            {
                bool found = false;
                int cnt;

                lock (TagInfoList)
                {
                    UInt16 ocRSSI = info.Bank1Data[1];   // Address d
                    UInt16 temp   = info.Bank1Data[2];   // Address e

                    for (cnt=0; cnt<TagInfoList.Count; cnt++)
                    {
                        if (TagInfoList[cnt].EPC==info.epc.ToString())
                        {
                            if (ocRSSI >= BleMvxApplication._rfMicro_minOCRSSI && ocRSSI <= BleMvxApplication._rfMicro_maxOCRSSI)
                            {
                                // if (temp >= 1300 && temp <= 3500)
                                // {
                                    UInt64 caldata = (UInt64)(((UInt64)info.Bank2Data[0]<<48) |
                                                              ((UInt64)info.Bank2Data[1]<<32) | 
                                                              ((UInt64)info.Bank2Data[2]<<16) | 
                                                              ((UInt64)info.Bank2Data[3]));

                                    if (caldata==0) { TagInfoList[cnt].SensorAvgValue = "NoCalData"; }
                                    else
                                    {
                                        string tEPC = TagInfoList[cnt].EPC.Substring(TagInfoList[cnt].EPC.Length - 4);
                                        double SAV = Math.Round(getTempC(temp, caldata), 5);

                                        // if (CORRECTION.ContainsKey(tEPC))
                                        // {
                                        //     SAV = SAV - CORRECTION[tEPC];
                                        // }
                                        string DisplaySAV = Math.Round(SAV, 2).ToString() + "°";

                                        DateTime dt = DateTime.Now;
                                        TagInfoList[cnt].SensorAvgValue = DisplaySAV;
                                        TagInfoList[cnt].OCRSSI = ocRSSI;
                                        TagInfoList[cnt].SucessCount++;

                                    }
                                // }
                                // else {}
                                // found = true;
                                // break;
                            }
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        RFMicroTagInfoViewModel item = new RFMicroTagInfoViewModel();
                        item.EPC = info.epc.ToString();
                        item.SensorAvgValue = "";
                        // item.SucessCount = 0;

                        item.SucessCount = 100;

                        item.DisplayName = item.EPC;
                        item.OCRSSI = ocRSSI;

                        if (ocRSSI >= BleMvxApplication._rfMicro_minOCRSSI && ocRSSI <= BleMvxApplication._rfMicro_maxOCRSSI)
                        {
                            if (temp >= 1300 && temp <= 3500)
                            {
                                UInt64 caldata = (UInt64)(((UInt64)info.Bank2Data[0]<<48) | ((UInt64)info.Bank2Data[1]<<32) | ((UInt64)info.Bank2Data[2]<<16) | ((UInt64)info.Bank2Data[3]));

                                if (caldata==0) { item.SensorAvgValue = "NoCalData"; }
                                else
                                {
                                    double SAV = Math.Round(getTempC(temp, caldata), 1);   
                                    string DisplaySAV = Math.Round(SAV, 2).ToString() + "°";

                                    // item.SucessCount++;

                                    item.SucessCount = 200;

                                    item.SensorAvgValue = DisplaySAV;

                                    List<string> t_data = new List<string>{ item.SensorAvgValue };
                                    List<string> t_RSSI = new List<string>{ item.OCRSSI.ToString() };

                                }
                            }
                        }
                        TagInfoList.Insert(0, item);
                    }
                }
            });
        }

        private void AddOrUpdateTagData(CSLibrary.Structures.TagCallbackInfo info)
        {
            InvokeOnMainThread(() =>
            {
                bool found = false;
                int cnt;

                lock (TagInfoList)
                {
                    UInt16 ocRSSI     = (UInt16)(info.Bank1Data[0] & 0x1ff);  // address c
                    UInt16 sensorCode = info.Bank1Data[1];                    // address d
                    UInt16 temp       = info.Bank1Data[2];                    // address e

                    for (cnt = 0; cnt < TagInfoList.Count; cnt++)
                    {
                        if (TagInfoList[cnt].EPC == info.epc.ToString())
                        {
                            TagInfoList[cnt].OCRSSI = ocRSSI;

                            if (ocRSSI >= BleMvxApplication._rfMicro_minOCRSSI && ocRSSI <= BleMvxApplication._rfMicro_maxOCRSSI)
                            {
                                // BleMvxApplication._rfMicro_SensorType // 0 = Sensor code, 1 = Temp
                                // BleMvxApplication._rfMicro_SensorUnit // 0=code, 1=f, 2=c, 3=%

                                switch (BleMvxApplication._rfMicro_SensorType)
                                {
                                    case 0:
                                        break;

                                    default:
                                        if (temp >= 1300 && temp <= 3500)
                                        {
                                            double SensorAvgValue;
                                            TagInfoList[cnt].SucessCount++;
                                            UInt64 caldata = (UInt64)(((UInt64)info.Bank2Data[0] << 48) | ((UInt64)info.Bank2Data[1] << 32) | ((UInt64)info.Bank2Data[2] << 16) | ((UInt64)info.Bank2Data[3]));

                                            if (caldata == 0)
                                            {
                                                TagInfoList[cnt].SensorAvgValue = "NoCalData";
                                            }
                                            else
                                            {
                                                switch (BleMvxApplication._rfMicro_SensorUnit)
                                                {
                                                    case 0: // code
                                                        TagInfoList[cnt]._sensorValueSum += temp;
                                                        SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2);
                                                        TagInfoList[cnt].SensorAvgValue = SensorAvgValue.ToString();
                                                        break;

                                                    case 2: // F
                                                        TagInfoList[cnt]._sensorValueSum += getTempF(temp, caldata);
                                                        SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2);
                                                        TagInfoList[cnt].SensorAvgValue = SensorAvgValue.ToString();
                                                        break;

                                                    default: // C
                                                        TagInfoList[cnt]._sensorValueSum += getTempC(temp, caldata);
                                                        SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2);
                                                        TagInfoList[cnt].SensorAvgValue = SensorAvgValue.ToString();
                                                        break;
                                                }
                                            }
                                        }
                                        break;
                                }
                            }
                            else {}
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        RFMicroTagInfoViewModel item = new RFMicroTagInfoViewModel();

                        item.EPC = info.epc.ToString();
                        item.NickName = GetNickName(item.EPC);
                        if (item.NickName != "")
                            item.DisplayName = item.NickName;
                        else
                            item.DisplayName = item.EPC;
                        item.OCRSSI = ocRSSI;
                        item.SucessCount = 0;
                        item._sensorValueSum = 0;
                        item.SensorAvgValue = "";

                        if (ocRSSI >= BleMvxApplication._rfMicro_minOCRSSI && ocRSSI <= BleMvxApplication._rfMicro_maxOCRSSI)
                        {

                            //BleMvxApplication._rfMicro_SensorType // 0 = Sensor code, 1 = Temp
                            //BleMvxApplication._rfMicro_SensorUnit //0=code, 1=f, 2=c, 3=%

                            switch (BleMvxApplication._rfMicro_SensorType)
                            {
                                case 0:
                                    break;

                                default:
                                    if (temp >= 1300 && temp <= 3500)
                                    {
                                        item.SucessCount++;
                                        UInt64 caldata = (UInt64)(((UInt64)info.Bank2Data[0] << 48) | ((UInt64)info.Bank2Data[1] << 32) | ((UInt64)info.Bank2Data[2] << 16) | ((UInt64)info.Bank2Data[3]));

                                        if (caldata == 0)
                                            item.SensorAvgValue = "NoCalData";
                                        else
                                            switch (BleMvxApplication._rfMicro_SensorUnit)
                                            {
                                                case 0: // code
                                                    item._sensorValueSum = temp;
                                                    item.SensorAvgValue = item._sensorValueSum.ToString();
                                                    break;

                                                case 2: // F
                                                    item._sensorValueSum = getTempF(temp, caldata);
                                                    item.SensorAvgValue = Math.Round(item._sensorValueSum, 2).ToString();
                                                    break;

                                                default: // C
                                                    item._sensorValueSum = getTempC(temp, caldata);
                                                    item.SensorAvgValue = Math.Round(item._sensorValueSum, 2).ToString();
                                                    break;
                                            }
                                    }
                                    break;
                            }
                        }
                        else { }

                        // _TagInfoList.Insert(0, item);
                        // RaisePropertyChanged(() => TagInfoList);

                        TagInfoList.Insert(0, item);

                        Trace.Message("EPC Data = {0}", item.EPC);
                        // https://github.com/xamarin/Xamarin.Forms/issues/3168
                    }
                }
            });
        }

        string GetNickName(string EPC)
        {
            for (int index = 0; index < ViewModelRFMicroNickname._TagNicknameList.Count; index++)
                if (ViewModelRFMicroNickname._TagNicknameList[index].EPC == EPC)
                    return ViewModelRFMicroNickname._TagNicknameList[index].Nickname;
            return "";
        }

        double getTempF(UInt16 temp, UInt64 CalCode)
        {
            return (getTemperatue(temp, CalCode) * 1.8 + 32.0);
        }

        double getTempC(UInt16 temp, UInt64 CalCode)
        {
            return getTemperatue(temp, CalCode);
        }

        double getTemperatue(UInt16 temp, UInt64 CalCode)
        {
            int crc = (int)(CalCode >> 48) & 0xffff;
            int calCode1 = (int)(CalCode >> 36) & 0x0fff;
            int calTemp1 = (int)(CalCode >> 25) & 0x07ff;
            int calCode2 = (int)(CalCode >> 13) & 0x0fff;
            int calTemp2 = (int)(CalCode >> 2) & 0x7FF;
            int calVer = (int)(CalCode & 0x03);

            double fTemperature = temp;
            fTemperature = ((double)calTemp2 - (double)calTemp1) * (fTemperature - (double)calCode1);
            fTemperature /= ((double)(calCode2) - (double)calCode1);
            fTemperature += (double)calTemp1;
            fTemperature -= 800;
            fTemperature /= 10;

            return fTemperature;
        }

        void VoltageEvent(object sender, CSLibrary.Notification.VoltageEventArgs e)
		{
            if (e.Voltage == 0xffff)
            {
                _labelVoltage = "Battery ERROR"; //	3.98v
            }
            else
            {
                // to fix CS108 voltage bug
                if (_cancelVoltageValue)
                {
                    _cancelVoltageValue = false;
                    return;
                }

                switch (BleMvxApplication._config.BatteryLevelIndicatorFormat)
                {
                    case 0:
                        _labelVoltage = "Battery " + ((double)e.Voltage / 1000).ToString("0.000") + "v"; //			v
                        break;

                    default:
                        _labelVoltage = "Battery " + ClassBattery.Voltage2Percent((double)e.Voltage / 1000).ToString("0") + "%"; //			%
                        break;
                }
            }

			RaisePropertyChanged(() => labelVoltage);
		}

        private void ShareDataButtonClick()
        {
            InvokeOnMainThread(() =>
            {
                string dataBase = "";

                lock (TagInfoList)
                {
                    for (int index = 0; index < TagInfoList.Count; index++)
                    {
                        dataBase += "\"" + TagInfoList[index].EPC + "\"," +
                                    "\"" + TagInfoList[index].NickName + "\"," +
                                    "\"" + ((BleMvxApplication._rfMicro_SensorType == 0) ? "Sensor code" : "Temperature") + "\"," +
                                    TagInfoList[index].SensorAvgValue + "," +
                                    "\"";
                        switch (BleMvxApplication._rfMicro_SensorUnit)
                        {
                            case 0:
                                dataBase += "RAW";
                                break;
                            case 1:
                                dataBase += "RAW";
                                break;
                            case 2:
                                dataBase += "F";
                                break;
                            case 3:
                                dataBase += "C";
                                break;
                            case 4:
                                dataBase += "%";
                                break;
                        }
                        dataBase += "\"";
                        ;
                    }
                }

                var r = CrossShare.Current.Share(new Plugin.Share.Abstractions.ShareMessage
                {
                    Text = dataBase,
                    Title = "Axzon tags list"
                });

                CSLibrary.Debug.WriteLine("BackupData : {0}", r.ToString());
            });
        }

        #region Key_event
        void HotKeys_OnKeyEvent(object sender, CSLibrary.Notification.HotKeyEventArgs e)
        {
            if (e.KeyCode == CSLibrary.Notification.Key.BUTTON)
            {
                if (e.KeyDown)
                {
                    StartInventory();
                }
                else
                {
                    StopInventory();
                }
            }
        }
        #endregion

        async void ShowDialog(string Msg)
        {
            var config = new ProgressDialogConfig()
            {
                Title = Msg,
                IsDeterministic = true,
                MaskType = MaskType.Gradient,
            };

            using (var progress = _userDialogs.Progress(config))
            {
                progress.Show();
                await System.Threading.Tasks.Task.Delay(1000);
            }
        }

    }
}
