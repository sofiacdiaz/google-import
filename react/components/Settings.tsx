/* eslint-disable @typescript-eslint/no-explicit-any */
import type { FC } from 'react'
import React, { useEffect, useState } from 'react'
import { useMutation, useQuery } from 'react-apollo'
import { FormattedMessage, useIntl } from 'react-intl'
import { Alert, Button, Card, Divider, Input, Toggle } from 'vtex.styleguide'

import APP_SETTINGS from '../queries/AppSettings.gql'
import SAVE_APP_SETTINGS from '../mutations/SaveAppSettings.gql'

const Settings: FC = () => {
  const { formatMessage } = useIntl()
  const [showAlert, setShowAlert] = useState(false)
  const [loading, setLoading] = useState(false)
  const [savedSuccess, setSavedSuccess] = useState(false)
  const [settingsState, setSettingsState] = useState({
    isV2Catalog: false,
    accountName: '',
  })

  const [saveSettings] = useMutation(SAVE_APP_SETTINGS)

  const {
    loading: settingsLoading,
    called: settingsCalled,
    data: settingsData,
  } = useQuery(APP_SETTINGS, {
    variables: {
      version: process.env.VTEX_APP_VERSION,
    },
    ssr: false,
  })

  useEffect(() => {
    if (!settingsData?.appSettings?.message) return

    const parsedSettings = JSON.parse(settingsData.appSettings.message)

    setSettingsState(parsedSettings)
  }, [settingsData])

  const displayAlert = !settingsLoading && settingsCalled && showAlert

  const handleSaveSettings = async () => {
    setLoading(true)

    await saveSettings({
      variables: {
        version: process.env.VTEX_APP_VERSION,
        settings: JSON.stringify(settingsState),
      },
    })
      .catch((err) => {
        console.error(err)
        setSavedSuccess(false)
      })
      .then(() => {
        setSavedSuccess(true)
      })
      .finally(() => {
        setShowAlert(true)
        setLoading(false)
      })
  }

  return (
    <div>
      {displayAlert && (
        <div className="mb2">
          <Alert
            type={savedSuccess ? 'success' : 'error'}
            onClose={() => setShowAlert(false)}
            autoClose={6000}
          >
            {savedSuccess ? (
              <FormattedMessage id="admin/sheets-catalog-import.settings.success" />
            ) : (
              <FormattedMessage id="admin/sheets-catalog-import.settings.error" />
            )}
          </Alert>
        </div>
      )}
      <Card>
        <div className="flex">
          <div className="w-70">
            <div className="flex">
              <div className="mt6 dib">
                <Toggle
                  label={formatMessage({
                    id: 'admin/sheets-catalog-import.settings.v2-catalog',
                  })}
                  size="large"
                  checked={settingsState.isV2Catalog}
                  onChange={() => {
                    setSettingsState({
                      ...settingsState,
                      isV2Catalog: !settingsState.isV2Catalog,
                    })
                  }}
                />
              </div>
            </div>
            <div className="flex">
              <div className="mv5">
                <Input
                  label={formatMessage({
                    id: 'admin/sheets-catalog-import.settings.account',
                  })}
                  onChange={(e: React.FormEvent<HTMLInputElement>) =>
                    setSettingsState({
                      ...settingsState,
                      accountName: e.currentTarget.value,
                    })
                  }
                  value={settingsState.accountName}
                  placeholder={formatMessage({
                    id: 'admin/sheets-catalog-import.settings.account',
                  })}
                  helpText={
                    <FormattedMessage id="admin/sheets-catalog-import.settings.accountHelp" />
                  }
                />
              </div>
            </div>
          </div>
          <div
            style={{ flexGrow: 1 }}
            className="flex items-stretch w-20 justify-center"
          >
            <Divider orientation="vertical" />
          </div>
          <div className="w-30 items-center flex">
            <Button
              variation="secondary"
              collapseLeft
              block
              isLoading={loading}
              onClick={() => {
                handleSaveSettings()
              }}
            >
              <FormattedMessage id="admin/sheets-catalog-import.settings.save" />
            </Button>
          </div>
        </div>
      </Card>
      <br />
    </div>
  )
}

export default Settings
