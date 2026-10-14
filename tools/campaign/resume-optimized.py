"""Resume frozen native evidence with a new simulator and the original calibrator.

Keeps all broker profiles in one calibration source family, without rewriting
the existing Aedes profile or changing the frozen native benchmark repository.
"""
import hashlib, json, pathlib, sys

root=pathlib.Path(sys.argv[sys.argv.index('--series')+1]).resolve()
plan=json.loads((root/'series.json').read_text(encoding='utf-8'))
sys.path.insert(0,str(pathlib.Path(plan['benchmarkRepository'])/'scripts/campaign'))
import run

class PinnedCalibratorCampaign(run.Campaign):
    def profile(self, broker):
        cli=pathlib.Path(self.plan['calibratorCli'])
        with cli.open('rb') as stream:
            if hashlib.file_digest(stream,'sha256').hexdigest()!=self.plan['calibratorCliSha256']:
                raise RuntimeError('Frozen calibrator CLI changed')
        original=(self.plan['simulatorCli'],self.plan['mqttYRepository'])
        try:
            self.plan['simulatorCli']=str(cli)
            self.plan['mqttYRepository']=self.plan['calibratorRepository']
            return super().profile(broker)
        finally:
            self.plan['simulatorCli'],self.plan['mqttYRepository']=original

    def execute(self):
        self.state['status']='running'
        self.state.pop('error',None)
        self.save()
        super().execute()

run.Campaign=PinnedCalibratorCampaign
run.main()
